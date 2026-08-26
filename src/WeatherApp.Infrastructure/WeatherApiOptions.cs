using System.ComponentModel.DataAnnotations;
using WeatherApp.Domain;

namespace WeatherApp.Infrastructure;

/// <summary>
/// Опции доступа к WeatherAPI, привязываемые из секции <see cref="SectionName"/> конфигурации.
/// Все ограничения валидируются на старте приложения (<c>ValidateOnStart</c>).
/// </summary>
public sealed class WeatherApiOptions : IValidatableObject
{
    /// <summary>Имя секции конфигурации, к которой привязываются опции ("WeatherApi").</summary>
    public const string SectionName = "WeatherApi";

    /// <summary>Базовый URL WeatherAPI. По умолчанию — публичный production-эндпоинт.</summary>
    [Required, Url]
    public string BaseUrl { get; init; } = "https://api.weatherapi.com/v1/";

    /// <summary>
    /// API-ключ WeatherAPI. Должен приходить из User Secrets или переменных окружения
    /// (например, <c>WeatherApi__ApiKey</c>); никогда не храните реальный ключ в source.
    /// </summary>
    [Required, MinLength(1)]
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Широта точки запроса. По умолчанию — Москва (см. <see cref="MoscowLocation.ExpectedLatitude"/>).</summary>
    public double Latitude { get; init; } = MoscowLocation.ExpectedLatitude;

    /// <summary>Долгота точки запроса. По умолчанию — Москва.</summary>
    public double Longitude { get; init; } = MoscowLocation.ExpectedLongitude;

    /// <summary>Горизонт прогноза в днях. Допускается только значение 3 — по требованию приложения.</summary>
    [Range(1, 3)]
    public int ForecastDays { get; init; } = 3;

    /// <summary>Таймаут запроса к провайдеру. Должен быть положительным и не превышать 1 минуту.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Дополнительная валидация инвариантов, которые не покрываются атрибутами:
    /// координаты — только московские, горизонт — ровно 3 дня, таймаут — в пределах минуты,
    /// BaseUrl — абсолютный HTTPS (или localhost в тестах).
    /// </summary>
    /// <param name="validationContext">Контекст валидации.</param>
    /// <returns>Список нарушений.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Latitude != MoscowLocation.ExpectedLatitude || Longitude != MoscowLocation.ExpectedLongitude)
        {
            yield return new ValidationResult("Only Moscow center coordinates are supported.", new[] { nameof(Latitude), nameof(Longitude) });
        }
        if (ForecastDays != 3)
        {
            yield return new ValidationResult("ForecastDays must be exactly 3.", new[] { nameof(ForecastDays) });
        }
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(1))
        {
            yield return new ValidationResult("Timeout must be greater than zero and no longer than one minute.", new[] { nameof(Timeout) });
        }
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && !string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ValidationResult("BaseUrl must be an absolute HTTPS URL (or localhost for tests).", new[] { nameof(BaseUrl) });
        }
    }
}