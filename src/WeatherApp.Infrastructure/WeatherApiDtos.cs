using System.Text.Json.Serialization;

namespace WeatherApp.Infrastructure;

/// <summary>
/// Корневой ответ WeatherAPI для эндпоинта <c>current.json</c>.
/// Содержит снимок локации и текущих погодных условий.
/// </summary>
internal sealed class WeatherApiCurrentResponse
{
    /// <summary>JSON: <c>location</c>. Локация, для которой получены данные.</summary>
    [JsonPropertyName("location")] public WeatherApiLocation? Location { get; init; }

    /// <summary>JSON: <c>current</c>. Снимок текущей погоды.</summary>
    [JsonPropertyName("current")] public WeatherApiCurrent? Current { get; init; }
}

/// <summary>
/// Корневой ответ WeatherAPI для эндпоинта <c>forecast.json</c>.
/// </summary>
internal sealed class WeatherApiForecastResponse
{
    /// <summary>JSON: <c>location</c>. Локация прогноза.</summary>
    [JsonPropertyName("location")] public WeatherApiLocation? Location { get; init; }

    /// <summary>JSON: <c>forecast</c>. Прогноз с почасовыми и дневными записями.</summary>
    [JsonPropertyName("forecast")] public WeatherApiForecast? Forecast { get; init; }
}

/// <summary>
/// Блок <c>location</c> в ответах WeatherAPI.
/// </summary>
internal sealed class WeatherApiLocation
{
    /// <summary>JSON: <c>name</c>. Имя города от провайдера.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>JSON: <c>localtime</c>. Локальное время в формате "yyyy-MM-dd H:mm".</summary>
    [JsonPropertyName("localtime")] public string? LocalTime { get; init; }

    /// <summary>JSON: <c>tz_id</c>. IANA-идентификатор таймзоны.</summary>
    [JsonPropertyName("tz_id")] public string? TimeZoneId { get; init; }
}

/// <summary>
/// Блок <c>current</c> — текущие погодные условия в ответе WeatherAPI.
/// </summary>
internal sealed class WeatherApiCurrent
{
    /// <summary>JSON: <c>temp_c</c>. Температура, °C.</summary>
    [JsonPropertyName("temp_c")] public double? TemperatureC { get; init; }

    /// <summary>JSON: <c>feelslike_c</c>. Ощущаемая температура, °C.</summary>
    [JsonPropertyName("feelslike_c")] public double? FeelsLikeC { get; init; }

    /// <summary>JSON: <c>condition</c>. Текст и иконка условий.</summary>
    [JsonPropertyName("condition")] public WeatherApiCondition? Condition { get; init; }

    /// <summary>JSON: <c>humidity</c>. Влажность, %.</summary>
    [JsonPropertyName("humidity")] public int? Humidity { get; init; }

    /// <summary>JSON: <c>wind_kph</c>. Скорость ветра, км/ч.</summary>
    [JsonPropertyName("wind_kph")] public double? WindKph { get; init; }

    /// <summary>JSON: <c>wind_dir</c>. Направление ветра.</summary>
    [JsonPropertyName("wind_dir")] public string? WindDirection { get; init; }

    /// <summary>JSON: <c>pressure_mb</c>. Давление, мбар.</summary>
    [JsonPropertyName("pressure_mb")] public double? PressureMb { get; init; }

    /// <summary>JSON: <c>last_updated</c>. Время наблюдения ("yyyy-MM-dd H:mm").</summary>
    [JsonPropertyName("last_updated")] public string? LastUpdated { get; init; }
}

/// <summary>
/// Блок <c>condition</c> в любом ответе WeatherAPI: текст + иконка.
/// </summary>
internal sealed class WeatherApiCondition
{
    /// <summary>JSON: <c>text</c>. Текстовое описание условий.</summary>
    [JsonPropertyName("text")] public string? Text { get; init; }

    /// <summary>JSON: <c>icon</c>. URL иконки (обычно протокол-relative).</summary>
    [JsonPropertyName("icon")] public string? IconUrl { get; init; }
}

/// <summary>
/// Блок <c>forecast</c> ответа <c>forecast.json</c>.
/// </summary>
internal sealed class WeatherApiForecast
{
    /// <summary>JSON: <c>forecastday</c>. Массив дневных сводок.</summary>
    [JsonPropertyName("forecastday")] public List<WeatherApiForecastDay>? Days { get; init; }
}

/// <summary>
/// Дневная сводка прогноза в ответе WeatherAPI.
/// </summary>
internal sealed class WeatherApiForecastDay
{
    /// <summary>JSON: <c>date</c>. Дата в формате "yyyy-MM-dd".</summary>
    [JsonPropertyName("date")] public string? Date { get; init; }

    /// <summary>JSON: <c>day</c>. Сводные данные за день.</summary>
    [JsonPropertyName("day")] public WeatherApiDay? Day { get; init; }

    /// <summary>JSON: <c>astro</c>. Астрономия (восход/закат).</summary>
    [JsonPropertyName("astro")] public WeatherApiAstro? Astro { get; init; }

    /// <summary>JSON: <c>hour</c>. Почасовые записи дня.</summary>
    [JsonPropertyName("hour")] public List<WeatherApiHour>? Hours { get; init; }
}

/// <summary>
/// Блок <c>day</c> в дневной сводке WeatherAPI: экстремумы и условия дня.
/// </summary>
internal sealed class WeatherApiDay
{
    /// <summary>JSON: <c>mintemp_c</c>. Минимальная температура дня, °C.</summary>
    [JsonPropertyName("mintemp_c")] public double? MinTemperatureC { get; init; }

    /// <summary>JSON: <c>maxtemp_c</c>. Максимальная температура дня, °C.</summary>
    [JsonPropertyName("maxtemp_c")] public double? MaxTemperatureC { get; init; }

    /// <summary>JSON: <c>avgtemp_c</c>. Средняя температура дня, °C.</summary>
    [JsonPropertyName("avgtemp_c")] public double? AverageTemperatureC { get; init; }

    /// <summary>JSON: <c>condition</c>. Условия дня.</summary>
    [JsonPropertyName("condition")] public WeatherApiCondition? Condition { get; init; }

    /// <summary>JSON: <c>daily_chance_of_rain</c>. Вероятность осадков за день, %.</summary>
    [JsonPropertyName("daily_chance_of_rain")] public int? ChanceOfRain { get; init; }
}

/// <summary>
/// Блок <c>astro</c>: восход и закат.
/// </summary>
internal sealed class WeatherApiAstro
{
    /// <summary>JSON: <c>sunrise</c>. Время восхода (HH:mm).</summary>
    [JsonPropertyName("sunrise")] public string? Sunrise { get; init; }

    /// <summary>JSON: <c>sunset</c>. Время заката (HH:mm).</summary>
    [JsonPropertyName("sunset")] public string? Sunset { get; init; }
}

/// <summary>
/// Почасовая запись в ответе WeatherAPI.
/// </summary>
internal sealed class WeatherApiHour
{
    /// <summary>JSON: <c>time</c>. Время часа ("yyyy-MM-dd H:mm").</summary>
    [JsonPropertyName("time")] public string? Time { get; init; }

    /// <summary>JSON: <c>temp_c</c>. Температура, °C.</summary>
    [JsonPropertyName("temp_c")] public double? TemperatureC { get; init; }

    /// <summary>JSON: <c>condition</c>. Условия часа.</summary>
    [JsonPropertyName("condition")] public WeatherApiCondition? Condition { get; init; }

    /// <summary>JSON: <c>chance_of_rain</c>. Вероятность осадков, %.</summary>
    [JsonPropertyName("chance_of_rain")] public int? ChanceOfRain { get; init; }

    /// <summary>JSON: <c>is_day</c>. 1 — день, 0 — ночь.</summary>
    [JsonPropertyName("is_day")] public int? IsDay { get; init; }
}

/// <summary>
/// Конверт ошибки WeatherAPI (HTTP 4xx/5xx, но с JSON-телом).
/// </summary>
internal sealed class WeatherApiErrorEnvelope
{
    /// <summary>JSON: <c>error</c>. Тело ошибки.</summary>
    [JsonPropertyName("error")] public WeatherApiError? Error { get; init; }
}

/// <summary>
/// Тело ошибки WeatherAPI.
/// </summary>
internal sealed class WeatherApiError
{
    /// <summary>JSON: <c>code</c>. Внутренний код ошибки провайдера.</summary>
    [JsonPropertyName("code")] public int? Code { get; init; }

    /// <summary>JSON: <c>message</c>. Сообщение провайдера об ошибке.</summary>
    [JsonPropertyName("message")] public string? Message { get; init; }
}