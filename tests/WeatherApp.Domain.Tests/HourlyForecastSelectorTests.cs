using FluentAssertions;
using WeatherApp.Domain;

namespace WeatherApp.Domain.Tests;

public sealed class MoscowLocationTests
{
    [Fact]
    public void ConstructorUsesExpectedCoordinates()
    {
        var location = new MoscowLocation();

        location.Latitude.Should().Be(55.7558);
        location.Longitude.Should().Be(37.6173);
    }

    [Fact]
    public void ConstructorRejectsOtherCoordinates()
    {
        Action act = () => _ = new MoscowLocation(0, 0);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*fixed Moscow coordinates*");
    }

    [Fact]
    public void QueryReturnsInvariantLatLonString()
    {
        var location = new MoscowLocation();

        location.Query.Should().Be("55.7558,37.6173");
    }
}

public sealed class WeatherIconNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeReturnsNullForEmptyInput(string? value)
    {
        WeatherIconNormalizer.Normalize(value).Should().BeNull();
    }

    [Fact]
    public void NormalizePrependsHttpsForProtocolRelativeUrl()
    {
        WeatherIconNormalizer.Normalize("//cdn.example.com/icon.png")
            .Should().Be("https://cdn.example.com/icon.png");
    }

    [Fact]
    public void NormalizeKeepsAbsoluteHttpsUrl()
    {
        WeatherIconNormalizer.Normalize("https://cdn.example.com/icon.png")
            .Should().Be("https://cdn.example.com/icon.png");
    }
}

public sealed class HourlyForecastSelectorTests
{
    private static HourWeatherData Hour(DateTimeOffset time) =>
        new(time, 1.0, new WeatherCondition(null, null), 0, true);

    [Fact]
    public void SelectThrowsWhenHoursMissing()
    {
        Action act = () => _ = HourlyForecastSelector.Select(
            new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero),
            currentDayHours: null,
            nextDayHours: [Hour(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero))]);

        act.Should().Throw<InvalidWeatherDataException>();
    }

    [Fact]
    public void SelectReturnsRemainingHoursForCurrentDayAndAllForNextDay()
    {
        var localTime = new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero);
        var currentDayHours = new List<HourWeatherData>
        {
            Hour(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero)),
            Hour(new DateTimeOffset(2026, 8, 25, 13, 0, 0, TimeSpan.Zero)),
            Hour(new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero)),
            Hour(new DateTimeOffset(2026, 8, 25, 15, 0, 0, TimeSpan.Zero)),
            Hour(new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero)),
        };
        var nextDayHours = Enumerable.Range(0, 24)
            .Select(h => Hour(new DateTimeOffset(2026, 8, 26, h, 0, 0, TimeSpan.Zero)))
            .ToList();

        var result = HourlyForecastSelector.Select(localTime, currentDayHours, nextDayHours);

        result.Should().HaveCount(27);
        result[0].Time.Hour.Should().Be(14);
        result[^1].Time.Day.Should().Be(26);
        result[^1].Time.Hour.Should().Be(23);
    }

    [Fact]
    public void SelectIncludesCurrentHourEvenWhenExactMatchExists()
    {
        var localTime = new DateTimeOffset(2026, 8, 25, 14, 30, 0, TimeSpan.Zero);
        var currentDayHours = new List<HourWeatherData>
        {
            Hour(new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero)),
            Hour(new DateTimeOffset(2026, 8, 25, 15, 0, 0, TimeSpan.Zero)),
        };
        var nextDayHours = new List<HourWeatherData>
        {
            Hour(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero)),
        };

        var result = HourlyForecastSelector.Select(localTime, currentDayHours, nextDayHours);

        result.Should().HaveCount(3);
        result[0].Time.Hour.Should().Be(14);
        result[1].Time.Hour.Should().Be(15);
        result[2].Time.Day.Should().Be(26);
    }

    [Fact]
    public void SelectHandlesDayRolloverAt23To00()
    {
        var localTime = new DateTimeOffset(2026, 8, 25, 23, 30, 0, TimeSpan.Zero);
        var currentDayHours = new List<HourWeatherData>
        {
            Hour(new DateTimeOffset(2026, 8, 25, 23, 0, 0, TimeSpan.Zero)),
        };
        var nextDayHours = Enumerable.Range(0, 24)
            .Select(h => Hour(new DateTimeOffset(2026, 8, 26, h, 0, 0, TimeSpan.Zero)))
            .ToList();

        var result = HourlyForecastSelector.Select(localTime, currentDayHours, nextDayHours);

        result.Should().HaveCount(25);
        result[0].Time.Should().Be(new DateTimeOffset(2026, 8, 25, 23, 0, 0, TimeSpan.Zero));
        result[^1].Time.Should().Be(new DateTimeOffset(2026, 8, 26, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void SelectThrowsWhenDuplicatesDetected()
    {
        var localTime = new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero);
        var currentDayHours = new List<HourWeatherData>
        {
            Hour(new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero)),
            Hour(new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero)),
        };
        var nextDayHours = new List<HourWeatherData>
        {
            Hour(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero)),
        };

        Action act = () => _ = HourlyForecastSelector.Select(localTime, currentDayHours, nextDayHours);

        act.Should().Throw<InvalidWeatherDataException>()
            .WithMessage("*duplicates*");
    }

    [Fact]
    public void SelectThrowsWhenNoHoursMatch()
    {
        var localTime = new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero);
        var currentDayHours = new List<HourWeatherData>
        {
            Hour(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero)),
        };
        var nextDayHours = new List<HourWeatherData>
        {
            Hour(new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero)),
        };

        Action act = () => _ = HourlyForecastSelector.Select(localTime, currentDayHours, nextDayHours);

        act.Should().Throw<InvalidWeatherDataException>();
    }

    [Fact]
    public void SelectReturnsHoursSortedChronologically()
    {
        var localTime = new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero);
        var currentDayHours = new List<HourWeatherData>
        {
            Hour(new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero)),
            Hour(new DateTimeOffset(2026, 8, 25, 15, 0, 0, TimeSpan.Zero)),
            Hour(new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero)),
        };
        var nextDayHours = Enumerable.Range(0, 24)
            .Select(h => Hour(new DateTimeOffset(2026, 8, 26, h, 0, 0, TimeSpan.Zero)))
            .ToList();

        var result = HourlyForecastSelector.Select(localTime, currentDayHours, nextDayHours);

        result.Should().BeInAscendingOrder(h => h.Time);
        new[] { result[0].Time.Hour, result[1].Time.Hour, result[2].Time.Hour }
            .Should().Equal(14, 15, 16);
    }
}