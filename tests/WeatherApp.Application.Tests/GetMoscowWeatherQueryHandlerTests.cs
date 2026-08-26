using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WeatherApp.Application;
using WeatherApp.Domain;

namespace WeatherApp.Application.Tests;

public sealed class GetMoscowWeatherQueryHandlerTests
{
    [Fact]
    public async Task HandleStartsCurrentAndForecastWithFixedLocationAndToken()
    {
        var provider = Substitute.For<IWeatherProvider>();
        var current = CreateCurrent();
        var forecast = CreateForecast();
        var token = new CancellationTokenSource().Token;
        provider.GetCurrentAsync(Arg.Any<MoscowLocation>(), token).Returns(Task.FromResult(current));
        provider.GetForecastAsync(Arg.Any<MoscowLocation>(), 3, token).Returns(Task.FromResult(forecast));
        var handler = new GetMoscowWeatherQueryHandler(
            provider,
            Substitute.For<ILogger<GetMoscowWeatherQueryHandler>>());

        var response = await handler.Handle(new GetMoscowWeatherQuery(), token);

        response.Location.City.Should().Be("Москва");
        response.Daily.Should().HaveCount(3);
        response.Hourly.Should().NotBeEmpty();
        await provider.Received(1).GetCurrentAsync(
            Arg.Is<MoscowLocation>(location => location.Query == "55.7558,37.6173"),
            token);
        await provider.Received(1).GetForecastAsync(
            Arg.Is<MoscowLocation>(location => location.Query == "55.7558,37.6173"),
            3,
            token);
    }

    [Fact]
    public async Task HandlePropagatesCancellation()
    {
        var provider = Substitute.For<IWeatherProvider>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        provider.GetCurrentAsync(Arg.Any<MoscowLocation>(), cancellation.Token)
            .Returns(Task.FromCanceled<CurrentWeatherData>(cancellation.Token));
        provider.GetForecastAsync(Arg.Any<MoscowLocation>(), 3, cancellation.Token)
            .Returns(Task.FromCanceled<WeatherForecastData>(cancellation.Token));
        var handler = new GetMoscowWeatherQueryHandler(
            provider,
            Substitute.For<ILogger<GetMoscowWeatherQueryHandler>>());

        Func<Task> act = () => handler.Handle(new GetMoscowWeatherQuery(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HandleTranslatesInvalidDomainData()
    {
        var provider = Substitute.For<IWeatherProvider>();
        provider.GetCurrentAsync(Arg.Any<MoscowLocation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateCurrent()));
        provider.GetForecastAsync(Arg.Any<MoscowLocation>(), 3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new WeatherForecastData(
                CreateForecast().Location,
                CreateForecast().Hours,
                [CreateDay(2026, 8, 25), CreateDay(2026, 8, 26)])));
        var handler = new GetMoscowWeatherQueryHandler(
            provider,
            Substitute.For<ILogger<GetMoscowWeatherQueryHandler>>());

        Func<Task> act = () => handler.Handle(new GetMoscowWeatherQuery(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<WeatherApplicationException>();
        exception.Which.Category.Should().Be(WeatherErrorCategory.InvalidProviderResponse);
    }

    [Fact]
    public async Task HandleTranslatesUnexpectedProviderFailure()
    {
        var provider = Substitute.For<IWeatherProvider>();
        provider.GetCurrentAsync(Arg.Any<MoscowLocation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CurrentWeatherData>(new InvalidOperationException("internal")));
        provider.GetForecastAsync(Arg.Any<MoscowLocation>(), 3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateForecast()));
        var handler = new GetMoscowWeatherQueryHandler(
            provider,
            Substitute.For<ILogger<GetMoscowWeatherQueryHandler>>());

        Func<Task> act = () => handler.Handle(new GetMoscowWeatherQuery(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<WeatherApplicationException>();
        exception.Which.Category.Should().Be(WeatherErrorCategory.ProviderUnavailable);
        exception.Which.Message.Should().Be("Unexpected error.");
    }

    private static CurrentWeatherData CreateCurrent() => new(
        20,
        19,
        new WeatherCondition("Ясно", "//cdn.example.com/current.png"),
        50,
        12,
        "N",
        755,
        new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero));

    private static WeatherForecastData CreateForecast()
    {
        var currentDate = new DateOnly(2026, 8, 25);
        var hours = Enumerable.Range(14, 10)
            .Select(hour => new HourWeatherData(
                new DateTimeOffset(2026, 8, 25, hour, 0, 0, TimeSpan.Zero),
                20,
                new WeatherCondition("Ясно", "//cdn.example.com/hour.png"),
                10,
                true))
            .Concat(Enumerable.Range(0, 24).Select(hour => new HourWeatherData(
                new DateTimeOffset(2026, 8, 26, hour, 0, 0, TimeSpan.Zero),
                20,
                new WeatherCondition("Облачно", null),
                20,
                true)))
            .ToArray();
        return new WeatherForecastData(
            new WeatherLocationData("Москва", new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero), "Europe/Moscow"),
            hours,
            [CreateDay(currentDate), CreateDay(2026, 8, 26), CreateDay(2026, 8, 27)]);
    }

    private static DailyWeatherData CreateDay(int year, int month, int day) =>
        CreateDay(new DateOnly(year, month, day));

    private static DailyWeatherData CreateDay(DateOnly date) => new(
        date,
        10,
        25,
        18,
        new WeatherCondition("Ясно", "//cdn.example.com/day.png"),
        15,
        new TimeOnly(5, 0),
        new TimeOnly(21, 0));
}