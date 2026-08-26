namespace WeatherApp.Contracts;

/// <summary>
/// Публичный ответ backend для запроса погоды в Москве.
/// Содержит снимок текущей погоды, почасовой прогноз на ближайшие часы
/// и трёхдневный прогноз для фиксированных координат 55.7558, 37.6173.
/// </summary>
/// <param name="Location">Локация, для которой получены данные (город, локальное время, таймзона).</param>
/// <param name="Current">Текущие погодные условия.</param>
/// <param name="Hourly">Почасовой прогноз: оставшиеся часы текущего дня + все часы следующего дня.</param>
/// <param name="Daily">Прогноз по дням на текущие и два последующих календарных дня.</param>
public sealed record WeatherResponse(
    WeatherLocation Location,
    CurrentWeather Current,
    IReadOnlyList<HourlyWeather> Hourly,
    IReadOnlyList<DailyWeather> Daily);

/// <summary>
/// Географическая и временная привязка ответа погоды.
/// </summary>
/// <param name="City">Название города, как его вернул провайдер (может быть null).</param>
/// <param name="LocalTime">Локальное время в точке наблюдения.</param>
/// <param name="TimeZoneId">IANA-идентификатор таймзоны (например, "Europe/Moscow"), либо null.</param>
public sealed record WeatherLocation(
    string? City,
    DateTimeOffset LocalTime,
    string? TimeZoneId);

/// <summary>
/// Снимок текущей погоды.
/// Все числовые поля могут быть null, если провайдер их не вернул.
/// </summary>
/// <param name="TemperatureC">Температура воздуха, °C.</param>
/// <param name="FeelsLikeC">Ощущаемая температура, °C.</param>
/// <param name="ConditionText">Текстовое описание условий (например, "Ясно").</param>
/// <param name="IconUrl">Полный HTTPS-URL иконки условий (нормализован из "//cdn..." провайдера).</param>
/// <param name="Humidity">Относительная влажность, %.</param>
/// <param name="WindKph">Скорость ветра, км/ч.</param>
/// <param name="WindDirection">Направление ветра в компактной форме (например, "NW").</param>
/// <param name="PressureMb">Атмосферное давление, мбар.</param>
/// <param name="UpdatedAt">Момент наблюдения по данным провайдера.</param>
public sealed record CurrentWeather(
    double? TemperatureC,
    double? FeelsLikeC,
    string? ConditionText,
    string? IconUrl,
    int? Humidity,
    double? WindKph,
    string? WindDirection,
    double? PressureMb,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Почасовой прогноз для одного часа.
/// </summary>
/// <param name="Time">Локальное время прогноза.</param>
/// <param name="TemperatureC">Температура воздуха в этот час, °C.</param>
/// <param name="ConditionText">Текстовое описание условий.</param>
/// <param name="IconUrl">HTTPS-URL иконки условий.</param>
/// <param name="ChanceOfRain">Вероятность осадков, %.</param>
/// <param name="IsDay">true — дневное время, false — ночное. null, если провайдер не указал.</param>
public sealed record HourlyWeather(
    DateTimeOffset Time,
    double? TemperatureC,
    string? ConditionText,
    string? IconUrl,
    int? ChanceOfRain,
    bool? IsDay);

/// <summary>
/// Прогноз на целый календарный день.
/// </summary>
/// <param name="Date">Дата прогноза (без времени).</param>
/// <param name="MinTemperatureC">Минимальная температура за день, °C.</param>
/// <param name="MaxTemperatureC">Максимальная температура за день, °C.</param>
/// <param name="AverageTemperatureC">Средняя температура за день, °C.</param>
/// <param name="ConditionText">Текстовое описание дневных условий.</param>
/// <param name="IconUrl">HTTPS-URL иконки условий.</param>
/// <param name="ChanceOfRain">Вероятность осадков за день, %.</param>
/// <param name="Sunrise">Локальное время восхода.</param>
/// <param name="Sunset">Локальное время заката.</param>
public sealed record DailyWeather(
    DateOnly Date,
    double? MinTemperatureC,
    double? MaxTemperatureC,
    double? AverageTemperatureC,
    string? ConditionText,
    string? IconUrl,
    int? ChanceOfRain,
    TimeOnly? Sunrise,
    TimeOnly? Sunset);