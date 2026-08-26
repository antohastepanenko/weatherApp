using Microsoft.AspNetCore.Diagnostics;
using WeatherApp.Application;

namespace WeatherApp.Api;

/// <summary>
/// Глобальный обработчик исключений для backend-приложения.
/// Преобразует доменные <see cref="WeatherApplicationException"/> в Problem Details
/// с безопасным для клиента сообщением, не раскрывая API-ключ, stack trace или URL запроса.
/// </summary>
/// <param name="logger">Логгер для записи ошибок.</param>
public sealed class WeatherExceptionHandler(ILogger<WeatherExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Пытается обработать исключение, возникшее в пайплайне.
    /// Возвращает true, если исключение обработано (всегда true, кроме случая отмены клиента).
    /// </summary>
    /// <param name="httpContext">HTTP-контекст текущего запроса.</param>
    /// <param name="exception">Возникшее исключение.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>true, если исключение обработано; false, если обработчик не взял на себя ответственность.</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var (statusCode, title, detail) = exception switch
        {
            WeatherApplicationException { Category: WeatherErrorCategory.Timeout } =>
                (StatusCodes.Status504GatewayTimeout, "Таймаут погодного провайдера", "Данные о погоде временно недоступны."),
            WeatherApplicationException =>
                (StatusCodes.Status502BadGateway, "Погодный провайдер недоступен", "Данные о погоде временно недоступны."),
            _ => (StatusCodes.Status500InternalServerError, "Непредвиденная ошибка", "Произошла непредвиденная ошибка.")
        };

        WeatherApiLog.RequestFailed(logger, exception, statusCode);
        httpContext.Response.StatusCode = statusCode;
        await Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier })
            .ExecuteAsync(httpContext);
        return true;
    }
}

/// <summary>
/// Структурированные сообщения логгера для обработчика исключений.
/// </summary>
internal static partial class WeatherApiLog
{
    /// <summary>Запрос погоды завершился ошибкой.</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Weather request failed with status {StatusCode}.")]
    public static partial void RequestFailed(ILogger logger, Exception exception, int statusCode);
}