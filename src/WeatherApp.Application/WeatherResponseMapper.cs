using WeatherApp.Contracts;
using WeatherApp.Domain;

namespace WeatherApp.Application;

/// <summary>
/// Маппер из доменных моделей провайдера в публичный <see cref="WeatherResponse"/>.
/// Внутри вызывает <see cref="HourlyForecastSelector"/> для среза почасового прогноза.
/// </summary>
public static class WeatherResponseMapper
{
    /// <summary>
    /// Собирает публичный ответ из текущих данных и прогноза.
    /// </summary>
    /// <param name="current">Текущая погода от провайдера.</param>
    /// <param name="forecast">Прогноз от провайдера (должен содержать не менее 3 дней).</param>
    /// <returns>Готовый <see cref="WeatherResponse"/> с почасовым и дневным прогнозами.</returns>
    /// <exception cref="InvalidWeatherDataException">Если в прогнозе меньше 3 дней.</exception>
    public static WeatherResponse Map(CurrentWeatherData current, WeatherForecastData forecast)
    {
        if (forecast.Days.Count < 3)
        {
            throw new InvalidWeatherDataException("Forecast must contain three calendar days.");
        }

        var currentDate = DateOnly.FromDateTime(forecast.Location.LocalTime.DateTime);
        var currentDayHours = forecast.Hours
            .Where(hour => DateOnly.FromDateTime(hour.Time.DateTime) == currentDate)
            .ToList();
        var nextDayHours = forecast.Hours
            .Where(hour => DateOnly.FromDateTime(hour.Time.DateTime) == currentDate.AddDays(1))
            .ToList();
        var hourly = HourlyForecastSelector.Select(forecast.Location.LocalTime, currentDayHours, nextDayHours);

        return new WeatherResponse(
            new WeatherLocation(forecast.Location.City, forecast.Location.LocalTime, forecast.Location.TimeZoneId),
            MapCurrent(current),
            hourly.Select(MapHourly).ToArray(),
            forecast.Days.Select(MapDaily).ToArray());
    }

    /// <summary>
    /// Маппит доменную текущую погоду в публичный контракт. Иконка нормализуется
    /// в абсолютный HTTPS через <see cref="WeatherIconNormalizer"/>.
    /// </summary>
    /// <param name="value">Доменная модель текущей погоды.</param>
    /// <returns>Публичная модель.</returns>
    private static CurrentWeather MapCurrent(CurrentWeatherData value) => new(
        value.TemperatureC,
        value.FeelsLikeC,
        value.Condition.Text,
        WeatherIconNormalizer.Normalize(value.Condition.IconUrl),
        value.Humidity,
        value.WindKph,
        value.WindDirection,
        value.PressureMb,
        value.UpdatedAt);

    /// <summary>
    /// Маппит одну почасовую запись в публичный контракт.
    /// </summary>
    /// <param name="value">Доменная почасовая запись.</param>
    /// <returns>Публичный почасовой прогноз.</returns>
    private static HourlyWeather MapHourly(HourWeatherData value) => new(
        value.Time,
        value.TemperatureC,
        value.Condition.Text,
        WeatherIconNormalizer.Normalize(value.Condition.IconUrl),
        value.ChanceOfRain,
        value.IsDay);

    /// <summary>
    /// Маппит одну дневную сводку в публичный контракт.
    /// </summary>
    /// <param name="value">Доменная дневная сводка.</param>
    /// <returns>Публичный прогноз на день.</returns>
    private static DailyWeather MapDaily(DailyWeatherData value) => new(
        value.Date,
        value.MinTemperatureC,
        value.MaxTemperatureC,
        value.AverageTemperatureC,
        value.Condition.Text,
        WeatherIconNormalizer.Normalize(value.Condition.IconUrl),
        value.ChanceOfRain,
        value.Sunrise,
        value.Sunset);
}