using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using WeatherApp.Application;
using WeatherApp.Contracts;
using WeatherApp.Domain;

namespace WeatherApp.Api.Tests;

public sealed class WeatherEndpointTests : IClassFixture<WeatherApiFactory>
{
    private readonly WeatherApiFactory factory;

    public WeatherEndpointTests(WeatherApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetReturnsWeatherResponseWithThreeSections()
    {
        factory.Provider = CreateProvider();

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/weather");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<WeatherResponse>();
        payload.Should().NotBeNull();
        payload!.Daily.Should().HaveCount(3);
        payload.Hourly.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetReturns502WhenProviderThrowsInvalidResponse()
    {
        var provider = Substitute.For<IWeatherProvider>();
        provider.GetCurrentAsync(Arg.Any<MoscowLocation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateCurrent()));
        provider.GetForecastAsync(Arg.Any<MoscowLocation>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<WeatherForecastData>>(_ => throw new WeatherApplicationException(
                WeatherErrorCategory.InvalidProviderResponse, "bad"));
        factory.Provider = provider;

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/weather");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsPayload>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(502);
        problem.Title.Should().Contain("провайдер");
    }

    [Fact]
    public async Task GetReturns504WhenProviderTimesOut()
    {
        var provider = Substitute.For<IWeatherProvider>();
        provider.GetCurrentAsync(Arg.Any<MoscowLocation>(), Arg.Any<CancellationToken>())
            .Returns<Task<CurrentWeatherData>>(_ => throw new WeatherApplicationException(
                WeatherErrorCategory.Timeout, "timeout"));
        provider.GetForecastAsync(Arg.Any<MoscowLocation>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateForecast()));
        factory.Provider = provider;

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/weather");

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    }

    [Fact]
    public async Task GetResponseDoesNotLeakProviderQueryUrlOrKey()
    {
        factory.Provider = CreateProvider();

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/weather");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("?key=");
        body.Should().NotContain("api.weatherapi.com");
    }

    private static CurrentWeatherData CreateCurrent() => new(
        20,
        19,
        new WeatherCondition("Ясно", "//cdn.example.com/c.png"),
        50,
        12,
        "N",
        755,
        new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero));

    private static WeatherForecastData CreateForecast()
    {
        var hours = Enumerable.Range(14, 10)
            .Select(hour => new HourWeatherData(
                new DateTimeOffset(2026, 8, 25, hour, 0, 0, TimeSpan.Zero),
                20,
                new WeatherCondition("Ясно", null),
                0,
                true))
            .Concat(Enumerable.Range(0, 24).Select(hour => new HourWeatherData(
                new DateTimeOffset(2026, 8, 26, hour, 0, 0, TimeSpan.Zero),
                20,
                new WeatherCondition("Облачно", null),
                0,
                true)))
            .ToArray();
        return new WeatherForecastData(
            new WeatherLocationData("Москва", new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero), "Europe/Moscow"),
            hours,
            new[]
            {
                new DailyWeatherData(new DateOnly(2026, 8, 25), 10, 25, 18, new WeatherCondition("Ясно", null), 0, new TimeOnly(5,0), new TimeOnly(21,0)),
                new DailyWeatherData(new DateOnly(2026, 8, 26), 11, 26, 19, new WeatherCondition("Ясно", null), 0, new TimeOnly(5,0), new TimeOnly(21,0)),
                new DailyWeatherData(new DateOnly(2026, 8, 27), 12, 27, 20, new WeatherCondition("Ясно", null), 0, new TimeOnly(5,0), new TimeOnly(21,0)),
            });
    }

    private static IWeatherProvider CreateProvider()
    {
        var provider = Substitute.For<IWeatherProvider>();
        provider.GetCurrentAsync(Arg.Any<MoscowLocation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateCurrent()));
        provider.GetForecastAsync(Arg.Any<MoscowLocation>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateForecast()));
        return provider;
    }
}

public sealed class WeatherApiFactory : WebApplicationFactory<Program>
{
    public IWeatherProvider Provider { get; set; } = CreateDefaultProvider();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WeatherApi:ApiKey"] = "test-key",
                ["WeatherApi:BaseUrl"] = "https://localhost:1/",
                ["Cors:AllowedOrigins:0"] = "https://localhost:7080"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWeatherProvider>();
            services.AddScoped<IWeatherProvider>(_ => Provider);
        });
    }

    private static IWeatherProvider CreateDefaultProvider()
    {
        var provider = Substitute.For<IWeatherProvider>();
        provider.GetCurrentAsync(Arg.Any<MoscowLocation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CurrentWeatherData(
                20,
                19,
                new WeatherCondition("Ясно", null),
                50,
                12,
                "N",
                755,
                new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero))));
        provider.GetForecastAsync(Arg.Any<MoscowLocation>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new WeatherForecastData(
                new WeatherLocationData("Москва", new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero), "Europe/Moscow"),
                Array.Empty<HourWeatherData>(),
                new[]
                {
                    new DailyWeatherData(new DateOnly(2026, 8, 25), 10, 25, 18, new WeatherCondition("Ясно", null), 0, new TimeOnly(5,0), new TimeOnly(21,0)),
                    new DailyWeatherData(new DateOnly(2026, 8, 26), 11, 26, 19, new WeatherCondition("Ясно", null), 0, new TimeOnly(5,0), new TimeOnly(21,0)),
                    new DailyWeatherData(new DateOnly(2026, 8, 27), 12, 27, 20, new WeatherCondition("Ясно", null), 0, new TimeOnly(5,0), new TimeOnly(21,0)),
                })));
        return provider;
    }
}

internal sealed record ProblemDetailsPayload(int? Status, string? Title, string? Detail)
{
    public int? Status { get; init; } = Status;
    public string? Title { get; init; } = Title;
    public string? Detail { get; init; } = Detail;
}