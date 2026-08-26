using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WeatherApp.Contracts;
using WeatherApp.Domain;

namespace WeatherApp.Application;

/// <summary>
/// Порт приложения к внешнему поставщику погоды.
/// Реализация находится в Infrastructure и инкапсулирует конкретный HTTP-клиент.
/// </summary>
public interface IWeatherProvider
{
    /// <summary>
    /// Запрашивает у провайдера текущие погодные условия.
    /// </summary>
    /// <param name="location">Локация запроса (на текущий момент — только Москва).</param>
    /// <param name="cancellationToken">Токен отмены, пробрасывается до HTTP-вызова.</param>
    /// <returns>Доменная модель текущей погоды.</returns>
    Task<CurrentWeatherData> GetCurrentAsync(MoscowLocation location, CancellationToken cancellationToken);

    /// <summary>
    /// Запрашивает у провайдера прогноз на <paramref name="days"/> дней.
    /// </summary>
    /// <param name="location">Локация запроса.</param>
    /// <param name="days">Горизонт прогноза в днях. В приложении всегда равен 3.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Доменная модель прогноза с почасовыми и дневными записями.</returns>
    Task<WeatherForecastData> GetForecastAsync(MoscowLocation location, int days, CancellationToken cancellationToken);
}

/// <summary>
/// Прогноз от провайдера: локация, все почасовые записи и дневные сводки.
/// </summary>
/// <param name="Location">Локация, для которой получен прогноз.</param>
/// <param name="Hours">Все почасовые записи в рамках запрошенного горизонта.</param>
/// <param name="Days">Дневные сводки (минимум 3 — иначе маппер бросит <see cref="InvalidWeatherDataException"/>).</param>
public sealed record WeatherForecastData(
    WeatherLocationData Location,
    IReadOnlyList<HourWeatherData> Hours,
    IReadOnlyList<DailyWeatherData> Days);

/// <summary>
/// Категории ошибок, которые приложение нормализует перед отдачей наружу.
/// Используются и exception handler'ом в API для маппинга в HTTP-коды.
/// </summary>
public enum WeatherErrorCategory
{
    /// <summary>Провайдер недоступен (сетевая ошибка, 5xx и т. п.).</summary>
    ProviderUnavailable,

    /// <summary>Истёк таймаут запроса к провайдеру.</summary>
    Timeout,

    /// <summary>Провайдер вернул неполный или синтаксически некорректный ответ.</summary>
    InvalidProviderResponse
}

/// <summary>
/// Доменное исключение уровня приложения. Содержит категорию, чтобы
/// вышестоящие слои могли корректно маппить её в HTTP-ответ или сообщение пользователю.
/// </summary>
/// <param name="category">Категория ошибки.</param>
/// <param name="message">Сообщение для логов / диагностики. Не отдаётся клиенту напрямую.</param>
/// <param name="innerException">О��игинальное исключение (например, из HTTP-стека).</param>
public sealed class WeatherApplicationException(
    WeatherErrorCategory category,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>Категория ошибки.</summary>
    public WeatherErrorCategory Category { get; } = category;
}

/// <summary>
/// Запрос MediatR на получение снимка погоды и трёхдневного прогноза для Москвы.
/// Без параметров: координаты зашиты в <see cref="Domain.MoscowLocation"/>.
/// </summary>
public sealed record GetMoscowWeatherQuery : IRequest<WeatherResponse>;

/// <summary>
/// Обработчик <see cref="GetMoscowWeatherQuery"/>.
/// Параллельно стартует запрос текущей погоды и прогноза на 3 дня,
/// маппит объединённый результат в <see cref="WeatherResponse"/>.
/// </summary>
/// <param name="weatherProvider">Порт к внешнему поставщику погоды.</param>
/// <param name="logger">Логгер для диагностических сообщений.</param>
public sealed class GetMoscowWeatherQueryHandler(
    IWeatherProvider weatherProvider,
    ILogger<GetMoscowWeatherQueryHandler> logger) : IRequestHandler<GetMoscowWeatherQuery, WeatherResponse>
{
    /// <summary>
    /// Обрабатывает запрос: получает текущую погоду и прогноз параллельно,
    /// нормализует исключения и маппит результат.
    /// </summary>
    /// <param name="request">Запрос (содержимое игнорируется).</param>
    /// <param name="cancellationToken">Токен отмены, пробрасывается в провайдер.</param>
    /// <returns>Готовый <see cref="WeatherResponse"/>.</returns>
    /// <exception cref="WeatherApplicationException">
    /// Любая ошибка провайдера нормализуется в это исключение с категорией
    /// <see cref="WeatherErrorCategory.InvalidProviderResponse"/>,
    /// <see cref="WeatherErrorCategory.ProviderUnavailable"/> или
    /// <see cref="WeatherErrorCategory.Timeout"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Если <paramref name="cancellationToken"/> был отменён.
    /// </exception>
    public async Task<WeatherResponse> Handle(GetMoscowWeatherQuery request, CancellationToken cancellationToken)
    {
        _ = request;
        var location = new MoscowLocation();
        var currentTask = weatherProvider.GetCurrentAsync(location, cancellationToken);
        var forecastTask = weatherProvider.GetForecastAsync(location, 3, cancellationToken);

        try
        {
            await Task.WhenAll(currentTask, forecastTask);
            return WeatherResponseMapper.Map(await currentTask, await forecastTask);
        }
        catch (WeatherApplicationException exception)
        {
            WeatherApplicationLog.ProviderError(logger, exception, exception.Category);
            throw;
        }
        catch (InvalidWeatherDataException exception)
        {
            throw new WeatherApplicationException(WeatherErrorCategory.InvalidProviderResponse, exception.Message, exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            WeatherApplicationLog.UnexpectedError(logger, exception);
            throw new WeatherApplicationException(WeatherErrorCategory.ProviderUnavailable, "Unexpected error.", exception);
        }
    }
}

/// <summary>
/// Сквозное поведение MediatR, измеряющее длительность каждого use-case'а
/// и логирующее результат (успех/ошибка) и затраченное время.
/// </summary>
/// <typeparam name="TRequest">Тип запроса.</typeparam>
/// <typeparam name="TResponse">Тип ответа.</typeparam>
/// <param name="logger">Логгер для записей о длительности.</param>
public sealed class UseCaseLoggingBehavior<TRequest, TResponse>(ILogger<UseCaseLoggingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Выполняет следующий обработчик и логирует длительность.
    /// Исключения пробрасываются дальше, но факт ошибки и длительность фиксируются.
    /// </summary>
    /// <param name="request">Запрос MediatR.</param>
    /// <param name="next">Ссылка на следующий обработчик в конвейере.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат следующего обработчика.</returns>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var response = await next(cancellationToken);
            WeatherApplicationLog.Completed(logger, typeof(TRequest).Name, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            return response;
        }
        catch (Exception)
        {
            WeatherApplicationLog.Failed(logger, typeof(TRequest).Name, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
    }
}

/// <summary>
/// Внутренний логгер-класс для структурированных сообщений уровня Application.
/// Все сообщения определены как <c>LoggerMessage</c>-атрибуты — без аллокации строк.
/// </summary>
internal static partial class WeatherApplicationLog
{
    /// <summary>Провайдер вернул ошибку.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Weather provider returned error: {Category}")]
    public static partial void ProviderError(ILogger logger, Exception exception, WeatherErrorCategory category);

    /// <summary>Неожиданное исключение в use-case'е (не <see cref="WeatherApplicationException"/> и не отмена).</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error while processing weather use case.")]
    public static partial void UnexpectedError(ILogger logger, Exception exception);

    /// <summary>Use-case успешно завершён.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Use case {Request} completed in {Duration} ms.")]
    public static partial void Completed(ILogger logger, string request, double duration);

    /// <summary>Use-case завершился ошибкой.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Use case {Request} failed after {Duration} ms.")]
    public static partial void Failed(ILogger logger, string request, double duration);
}

/// <summary>
/// Расширения для регистрации слоя Application в DI-контейнере.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует MediatR с обработчиками из текущей сборки и подключает
    /// <see cref="UseCaseLoggingBehavior{TRequest, TResponse}"/> как сквозное поведение.
    /// </summary>
    /// <param name="services">Коллекция сервисов.</param>
    /// <returns>Та же коллекция — для цепочного вызова.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<GetMoscowWeatherQueryHandler>();
            cfg.AddOpenBehavior(typeof(UseCaseLoggingBehavior<,>));
        });
        return services;
    }
}