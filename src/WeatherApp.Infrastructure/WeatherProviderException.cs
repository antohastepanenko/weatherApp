using System.Net;
using WeatherApp.Application;

namespace WeatherApp.Infrastructure;

/// <summary>
/// Исключение, описывающее ошибку уровня провайдера погоды.
/// Содержит <see cref="Category"/> для нормализации в API-слое и опциональный
/// <see cref="StatusCode"/> исходного HTTP-ответа.
/// </summary>
/// <param name="category">Категория ошибки.</param>
/// <param name="message">Сообщение для логов / диагностики. Не отдаётся клиенту напрямую.</param>
/// <param name="statusCode">HTTP-статус ответа провайдера, если применимо.</param>
/// <param name="innerException">Оригинальное исключение из HTTP-стека.</param>
public sealed class WeatherProviderException(
    WeatherErrorCategory category,
    string message,
    HttpStatusCode? statusCode = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>Категория ошибки.</summary>
    public WeatherErrorCategory Category { get; } = category;

    /// <summary>HTTP-статус ответа провайдера, если применимо.</summary>
    public HttpStatusCode? StatusCode { get; } = statusCode;
}