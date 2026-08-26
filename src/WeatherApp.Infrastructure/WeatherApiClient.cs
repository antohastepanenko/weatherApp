using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeatherApp.Application;
using WeatherApp.Domain;

namespace WeatherApp.Infrastructure;

/// <summary>
/// Структурированные сообщения логгера для <see cref="WeatherApiClient"/>.
/// Все сообщения определены через <c>LoggerMessage</c>-атрибуты.
/// </summary>
internal static partial class WeatherApiClientLog
{
    /// <summary>Запрос к WeatherAPI завершён.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "WeatherAPI request {Endpoint} returned {StatusCode} in {DurationMs} ms.")]
    public static partial void RequestCompleted(ILogger logger, string endpoint, int statusCode, double durationMs);
}

/// <summary>
/// Реализация <see cref="IWeatherProvider"/> поверх HTTP-клиента.
/// Использует встроенный resilience-пайплайн (retry на 408/429/5xx)
/// и нормализует любые ошибки в <see cref="WeatherProviderException"/>.
/// </summary>
/// <param name="httpClient">HTTP-клиент с настроенным <c>BaseAddress</c> и стандартным resilience-обработчиком.</param>
/// <param name="options">Опции доступа к WeatherAPI (см. <see cref="WeatherApiOptions"/>).</param>
/// <param name="logger">Логгер для диагностических сообщений.</param>
public sealed class WeatherApiClient(
    HttpClient httpClient,
    IOptions<WeatherApiOptions> options,
    ILogger<WeatherApiClient> logger) : IWeatherProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WeatherApiOptions settings = options.Value;

    /// <summary>
    /// Запрашивает у WeatherAPI текущую погоду (<c>current.json</c>).
    /// </summary>
    /// <param name="location">Локация (на текущий момент — только Москва).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Доменная модель текущей погоды.</returns>
    /// <exception cref="WeatherProviderException">При любой ошибке сети/JSON/протокола.</exception>
    public async Task<CurrentWeatherData> GetCurrentAsync(MoscowLocation location, CancellationToken cancellationToken)
    {
        var response = await SendAsync<WeatherApiCurrentResponse>("current.json", location, null, cancellationToken);
        if (response.Location is null || response.Current?.Condition is null)
        {
            throw new WeatherProviderException(WeatherErrorCategory.InvalidProviderResponse, "Current weather response is incomplete.");
        }

        var timeZoneId = response.Location.TimeZoneId;
        return new CurrentWeatherData(
            response.Current.TemperatureC,
            response.Current.FeelsLikeC,
            new WeatherCondition(response.Current.Condition.Text, response.Current.Condition.IconUrl),
            response.Current.Humidity,
            response.Current.WindKph,
            response.Current.WindDirection,
            response.Current.PressureMb,
            ParseTimestamp(response.Current.LastUpdated, timeZoneId));
    }

    /// <summary>
    /// Запрашивает у WeatherAPI прогноз на <paramref name="days"/> дней (<c>forecast.json</c>).
    /// </summary>
    /// <param name="location">Локация (на текущий момент — только Москва).</param>
    /// <param name="days">Горизонт прогноза. В приложении всегда 3.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Доменная модель прогноза.</returns>
    /// <exception cref="WeatherProviderException">При любой ошибке сети/JSON/протокола или неполном ответе.</exception>
    public async Task<WeatherForecastData> GetForecastAsync(MoscowLocation location, int days, CancellationToken cancellationToken)
    {
        var response = await SendAsync<WeatherApiForecastResponse>("forecast.json", location, days, cancellationToken);
        if (response.Location is null || response.Forecast?.Days is null || response.Forecast.Days.Count < 3)
        {
            throw new WeatherProviderException(WeatherErrorCategory.InvalidProviderResponse, "Forecast response is incomplete.");
        }

        var timeZoneId = response.Location.TimeZoneId;
        var localTime = ParseTimestamp(response.Location.LocalTime, timeZoneId)
            ?? throw new WeatherProviderException(WeatherErrorCategory.InvalidProviderResponse, "Forecast local time is invalid.");
        var daysResult = response.Forecast.Days.Select(day => MapDay(day, timeZoneId)).ToArray();
        var hours = response.Forecast.Days.SelectMany(day => (day.Hours ?? []).Select(hour => MapHour(hour, timeZoneId))).ToArray();
        return new WeatherForecastData(new WeatherLocationData(response.Location.Name, localTime, timeZoneId), hours, daysResult);
    }

    /// <summary>
    /// Общий метод HTTP-вызова: формирует query, отправляет GET, читает JSON.
    /// Нормализует все сетевые ошибки и ошибки парсинга в <see cref="WeatherProviderException"/>.
    /// </summary>
    /// <typeparam name="T">Тип ожидаемого JSON-ответа.</typeparam>
    /// <param name="endpoint">Относительный путь эндпоинта WeatherAPI.</param>
    /// <param name="location">Локация для query-параметра <c>q</c>.</param>
    /// <param name="days">Горизонт прогноза в днях (если применимо к эндпоинту).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Десериализованный ответ.</returns>
    /// <exception cref="WeatherProviderException">
    /// Категории:
    /// <see cref="WeatherErrorCategory.InvalidProviderResponse"/> для 4xx (кроме 408/429) и проблем JSON;
    /// <see cref="WeatherErrorCategory.ProviderUnavailable"/> для 408/429/5xx и сетевых ошибок;
    /// <see cref="WeatherErrorCategory.Timeout"/> при истечении таймаута без отмены.
    /// </exception>
    private async Task<T> SendAsync<T>(string endpoint, MoscowLocation location, int? days, CancellationToken cancellationToken)
    {
        var query = $"?key={Uri.EscapeDataString(settings.ApiKey)}&q={Uri.EscapeDataString(location.Query)}&lang=ru";
        if (days.HasValue)
        {
            query += $"&days={days.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        var requestUri = new Uri(endpoint + query, UriKind.Relative);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            stopwatch.Stop();
            WeatherApiClientLog.RequestCompleted(logger, endpoint, (int)response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
            if (!response.IsSuccessStatusCode)
            {
                var category = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500
                    ? WeatherErrorCategory.ProviderUnavailable
                    : WeatherErrorCategory.InvalidProviderResponse;
                throw new WeatherProviderException(category, "WeatherAPI returned an error.", response.StatusCode);
            }

            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return result ?? throw new WeatherProviderException(WeatherErrorCategory.InvalidProviderResponse, "WeatherAPI returned an empty response.");
        }
        catch (WeatherProviderException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WeatherProviderException(WeatherErrorCategory.Timeout, "WeatherAPI request timed out.", null, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new WeatherProviderException(WeatherErrorCategory.ProviderUnavailable, "WeatherAPI is unavailable.", null, exception);
        }
        catch (JsonException exception)
        {
            throw new WeatherProviderException(WeatherErrorCategory.InvalidProviderResponse, "WeatherAPI returned invalid JSON.", null, exception);
        }
    }

    /// <summary>
    /// Парсит строку вида "yyyy-MM-dd H:mm" в <see cref="DateTimeOffset"/>.
    /// Если передан IANA-идентификатор таймзоны (<paramref name="timeZoneId"/>),
    /// результат создаётся с реальным смещением этой зоны для указанного момента
    /// (это важно для провайдеров, которые отдают время в локальной таймзоне —
    /// например, WeatherAPI отдаёт московское время для Москвы).
    /// </summary>
    /// <param name="value">Исходная строка.</param>
    /// <param name="timeZoneId">
    /// IANA-идентификатор таймзоны (например, <c>Europe/Moscow</c>), либо null.
    /// Если зона неизвестна или не указана, откатывается на UTC (как было раньше).
    /// </param>
    /// <returns>Распарсенный timestamp или null, если строка пустая/некорректная.</returns>
    private static DateTimeOffset? ParseTimestamp(string? value, string? timeZoneId)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        if (timeZoneId is null || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
        {
            return new DateTimeOffset(local, TimeSpan.Zero);
        }
        
        var offset = zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    /// <summary>
    /// Преобразует почасовую запись провайдера в доменную модель.
    /// </summary>
    /// <param name="value">Запись WeatherAPI.</param>
    /// <param name="timeZoneId">IANA-зона, в которой провайдер отдал <c>time</c> (для Москвы — <c>Europe/Moscow</c>).</param>
    /// <returns>Доменная почасовая запись.</returns>
    /// <exception cref="WeatherProviderException">Если <c>time</c> нераспарсился.</exception>
    private static HourWeatherData MapHour(WeatherApiHour value, string? timeZoneId)
    {
        var time = ParseTimestamp(value.Time, timeZoneId) ?? throw new WeatherProviderException(WeatherErrorCategory.InvalidProviderResponse, "Forecast hour timestamp is invalid.");
        return new HourWeatherData(time, value.TemperatureC, new WeatherCondition(value.Condition?.Text, value.Condition?.IconUrl), value.ChanceOfRain, value.IsDay.HasValue ? value.IsDay == 1 : null);
    }

    /// <summary>
    /// Преобразует дневную сводку провайдера в доменную модель.
    /// </summary>
    /// <param name="value">Сводка WeatherAPI.</param>
    /// <param name="timeZoneId">IANA-зона для часов в <paramref name="value"/>.</param>
    /// <returns>Доменная дневная запись.</returns>
    /// <exception cref="WeatherProviderException">Если <c>date</c> нераспарсился.</exception>
    private static DailyWeatherData MapDay(WeatherApiForecastDay value, string? timeZoneId)
    {
        if (!DateOnly.TryParse(value.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new WeatherProviderException(WeatherErrorCategory.InvalidProviderResponse, "Forecast date is invalid.");
        }

        return new DailyWeatherData(date, value.Day?.MinTemperatureC, value.Day?.MaxTemperatureC, value.Day?.AverageTemperatureC,
            new WeatherCondition(value.Day?.Condition?.Text, value.Day?.Condition?.IconUrl), value.Day?.ChanceOfRain,
            ParseTime(value.Astro?.Sunrise), ParseTime(value.Astro?.Sunset));
    }

    /// <summary>
    /// Парсит строку времени "HH:mm" в <see cref="TimeOnly"/>.
    /// </summary>
    /// <param name="value">Исходная строка.</param>
    /// <returns>Распарсенное значение или null, если строка пустая/некорректная.</returns>
    private static TimeOnly? ParseTime(string? value) => TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result : null;
}
