namespace WeatherApp.Domain;

/// <summary>
/// Фиксированная локация приложения — центр Москвы.
/// Приложение спроектировано только под эти координаты;
/// попытка использовать любые другие значения приводит к <see cref="ArgumentException"/>.
/// </summary>
public sealed record MoscowLocation
{
    /// <summary>Широта центра Москвы (55.7558).</summary>
    public const double ExpectedLatitude = 55.7558;

    /// <summary>Долгота центра Москвы (37.6173).</summary>
    public const double ExpectedLongitude = 37.6173;

    /// <summary>
    /// Создаёт локацию. Принимает только координаты центра Москвы.
    /// </summary>
    /// <param name="latitude">Широта; должна строго равняться <see cref="ExpectedLatitude"/>.</param>
    /// <param name="longitude">Долгота; должна строго равняться <see cref="ExpectedLongitude"/>.</param>
    /// <exception cref="ArgumentException">Если переданные координаты не московские.</exception>
    public MoscowLocation(double latitude = ExpectedLatitude, double longitude = ExpectedLongitude)
    {
        if (latitude != ExpectedLatitude || longitude != ExpectedLongitude)
        {
            throw new ArgumentException("Only the fixed Moscow coordinates are supported.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Широта локации.</summary>
    public double Latitude { get; }

    /// <summary>Долгота локации.</summary>
    public double Longitude { get; }

    /// <summary>
    /// Координаты в формате WeatherAPI ("lat,lon") с инвариантной культурой.
    /// Используется при сборке query-string к провайдеру.
    /// </summary>
    public string Query => $"{Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Текстовое и иконочное описание погодных условий, полученное от провайдера.
/// </summary>
/// <param name="Text">Текст условий (например, "Облачно"). Может быть null.</param>
/// <param name="IconUrl">URL иконки. WeatherAPI отдаёт протокол-relative адрес ("//cdn..."); нормализуется через <see cref="WeatherIconNormalizer"/>.</param>
public sealed record WeatherCondition(string? Text, string? IconUrl);

/// <summary>
/// Снимок текущей погоды в доменном представлении.
/// Числовые поля могут быть null — провайдер не гарантирует полноту данных.
/// </summary>
/// <param name="TemperatureC">Температура воздуха, °C.</param>
/// <param name="FeelsLikeC">Ощущаемая температура, °C.</param>
/// <param name="Condition">Текст и иконка условий.</param>
/// <param name="Humidity">Относительная влажность, %.</param>
/// <param name="WindKph">Скорость ветра, км/ч.</param>
/// <param name="WindDirection">Направление ветра в компактной форме.</param>
/// <param name="PressureMb">Атмосферное давление, мбар.</param>
/// <param name="UpdatedAt">Момент наблюдения.</param>
public sealed record CurrentWeatherData(
    double? TemperatureC,
    double? FeelsLikeC,
    WeatherCondition Condition,
    int? Humidity,
    double? WindKph,
    string? WindDirection,
    double? PressureMb,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Почасовой прогноз в доменном представлении.
/// </summary>
/// <param name="Time">Локальное время прогноза.</param>
/// <param name="TemperatureC">Температура воздуха, °C.</param>
/// <param name="Condition">Текст и иконка условий.</param>
/// <param name="ChanceOfRain">Вероятность осадков, %.</param>
/// <param name="IsDay">true — дневное время, false — ночное, null — неизвестно.</param>
public sealed record HourWeatherData(
    DateTimeOffset Time,
    double? TemperatureC,
    WeatherCondition Condition,
    int? ChanceOfRain,
    bool? IsDay);

/// <summary>
/// Прогноз на целый день в доменном представлении.
/// </summary>
/// <param name="Date">Дата прогноза (без времени).</param>
/// <param name="MinTemperatureC">Минимальная температура за день, °C.</param>
/// <param name="MaxTemperatureC">Максимальная температура за день, °C.</param>
/// <param name="AverageTemperatureC">Средняя температура за день, °C.</param>
/// <param name="Condition">Текст и иконка дневных условий.</param>
/// <param name="ChanceOfRain">Вероятность осадков за день, %.</param>
/// <param name="Sunrise">Локальное время восхода.</param>
/// <param name="Sunset">Локальное время заката.</param>
public sealed record DailyWeatherData(
    DateOnly Date,
    double? MinTemperatureC,
    double? MaxTemperatureC,
    double? AverageTemperatureC,
    WeatherCondition Condition,
    int? ChanceOfRain,
    TimeOnly? Sunrise,
    TimeOnly? Sunset);

/// <summary>
/// Локация, возвращаемая провайдером: имя, локальное время и таймзона.
/// </summary>
/// <param name="City">Название города (может быть null).</param>
/// <param name="LocalTime">Локальное время в точке наблюдения.</param>
/// <param name="TimeZoneId">IANA-идентификатор таймзоны (например, "Europe/Moscow"), либо null.</param>
public sealed record WeatherLocationData(string? City, DateTimeOffset LocalTime, string? TimeZoneId);

/// <summary>
/// Объединённый результат провайдера: локация, текущая погода и почасовой/дневной прогнозы.
/// Используется как промежуточное звено между провайдером и маппером.
/// </summary>
/// <param name="Location">Локация, для которой получены данные.</param>
/// <param name="Current">Текущая погода.</param>
/// <param name="Hours">Все почасовые записи запрошенного периода.</param>
/// <param name="Days">Дневные сводки.</param>
public sealed record WeatherProviderResult(
    WeatherLocationData Location,
    CurrentWeatherData Current,
    IReadOnlyList<HourWeatherData> Hours,
    IReadOnlyList<DailyWeatherData> Days);

/// <summary>
/// Нормализация URL иконок, возвращаемых WeatherAPI.
/// WeatherAPI отдаёт протокол-relative адреса вида "//cdn.weatherapi.com/...";
/// для использования в HTML такие ссылки нужно привести к абсолютному https.
/// </summary>
public static class WeatherIconNormalizer
{
    /// <summary>
    /// Приводит относительный URL иконки к абсолютному HTTPS.
    /// </summary>
    /// <param name="iconUrl">Исходный URL от провайдера.</param>
    /// <returns>
    /// null, если вход пустой; абсолютный https-URL, если вход начинается с "//";
    /// иначе — исходный URL без изменений.
    /// </returns>
    public static string? Normalize(string? iconUrl) =>
        string.IsNullOrWhiteSpace(iconUrl)
            ? null
            : iconUrl.StartsWith("//", StringComparison.Ordinal)
                ? $"https:{iconUrl}"
                : iconUrl;
}

/// <summary>
/// Исключение, сигнализирующее о том, что данные, пришедшие от провайдера,
/// не соответствуют инвариантам приложения (например, отсутствуют нужные даты
/// или в почасовом прогнозе есть дубли по времени).
/// </summary>
/// <param name="message">Описание нарушенного инварианта.</param>
public sealed class InvalidWeatherDataException(string message) : Exception(message);