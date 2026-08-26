namespace WeatherApp.Domain;

/// <summary>
/// Чистая функция выбора часов из суточных массивов, возвращаемых провайдером.
/// Используется при формировании почасового прогноза в API.
/// </summary>
public static class HourlyForecastSelector
{
    /// <summary>
    /// Возвращает часы по правилу: оставшиеся часы текущего дня (начиная с локального часа
    /// <paramref name="localTime"/>) плюс все часы следующего календарного дня, в хронологическом порядке.
    /// </summary>
    /// <param name="localTime">Локальное время точки наблюдения (обычный источник — <c>location.localtime</c> провайдера).</param>
    /// <param name="currentDayHours">Часы текущего дня. Не может быть null.</param>
    /// <param name="nextDayHours">Часы следующего дня. Не может быть null.</param>
    /// <returns>
    /// Список часов в хронологическом порядке без дубликатов по <c>Time</c>.
    /// </returns>
    /// <exception cref="InvalidWeatherDataException">
    /// Если <paramref name="currentDayHours"/> или <paramref name="nextDayHours"/> равны null,
    /// если итоговый список пуст, или если в нём обнаружены дубли по времени.
    /// </exception>
    public static IReadOnlyList<HourWeatherData> Select(
        DateTimeOffset localTime,
        IReadOnlyList<HourWeatherData>? currentDayHours,
        IReadOnlyList<HourWeatherData>? nextDayHours)
    {
        if (currentDayHours is null || nextDayHours is null)
        {
            throw new InvalidWeatherDataException("Hourly forecast days are missing.");
        }

        var currentDate = DateOnly.FromDateTime(localTime.DateTime);
        var nextDate = currentDate.AddDays(1);
        var currentHour = localTime.Hour;
        var result = currentDayHours
            .Where(hour => DateOnly.FromDateTime(hour.Time.DateTime) == currentDate && hour.Time.Hour >= currentHour)
            .Concat(nextDayHours.Where(hour => DateOnly.FromDateTime(hour.Time.DateTime) == nextDate))
            .OrderBy(hour => hour.Time)
            .ToList();

        var hasDuplicateTimes = result
            .Zip(result.Skip(1), (previous, current) => previous.Time == current.Time)
            .Any(isDuplicate => isDuplicate);
        if (result.Count == 0 || hasDuplicateTimes)
        {
            throw new InvalidWeatherDataException("Hourly forecast does not contain the required dates or contains duplicates.");
        }

        return result;
    }
}