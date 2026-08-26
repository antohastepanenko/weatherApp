using System.Net;
using System.Net.Http.Json;
using WeatherApp.Contracts;

namespace WeatherApp.Web.Services;

/// <summary>
/// Контракт клиента к backend WeatherApp.
/// </summary>
public interface IWeatherBackendClient
{
    /// <summary>
    /// Запрашивает у backend текущую погоду и прогноз для Москвы.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены (например, при повторном клике «Обновить»).</param>
    /// <returns>Полный <see cref="WeatherResponse"/>.</returns>
    /// <exception cref="WeatherBackendException">При любой ошибке HTTP/пустом ответе.</exception>
    Task<WeatherResponse> GetWeatherAsync(CancellationToken cancellationToken);
}

/// <summary>
/// HTTP-реализация <see cref="IWeatherBackendClient"/>.
/// Маппит коды ответа backend в человекочитаемые сообщения для UI.
/// </summary>
/// <param name="httpClient">HTTP-клиент с настроенным <c>BaseAddress</c> на backend.</param>
public sealed class WeatherBackendClient(HttpClient httpClient) : IWeatherBackendClient
{
    /// <summary>
    /// Выполняет GET <c>api/weather</c> и возвращает <see cref="WeatherResponse"/>.
    /// При ошибке бросает <see cref="WeatherBackendException"/> с сообщением,
    /// безопасным для показа пользователю.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Полный ответ backend.</returns>
    /// <exception cref="WeatherBackendException">
    /// Если backend вернул неуспешный код или пустое тело.
    /// </exception>
    public async Task<WeatherResponse> GetWeatherAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("api/weather", cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<WeatherResponse>(cancellationToken)
                ?? throw new WeatherBackendException("Сервер вернул пустой ответ.");
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.GatewayTimeout => "Сервис погоды отвечает слишком долго. Повторите попытку.",
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable => "Сервис погоды временно недоступен. Повторите попытку.",
            _ => "Не удалось загрузить погоду. Повторите попытку."
        };
        throw new WeatherBackendException(message);
    }
}

/// <summary>
/// Исключение уровня клиента WeatherApp, сигнализирующее UI о необходимости
/// показать сообщение об ошибке и предложить повторить попытку.
/// </summary>
/// <param name="message">Готовое человекочитаемое сообщение для пользователя.</param>
public sealed class WeatherBackendException(string message) : Exception(message);