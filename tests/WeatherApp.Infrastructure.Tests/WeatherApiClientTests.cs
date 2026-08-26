using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WeatherApp.Application;
using WeatherApp.Domain;
using WeatherApp.Infrastructure;

namespace WeatherApp.Infrastructure.Tests;

public sealed class WeatherApiClientTests
{
    [Fact]
    public async Task GetCurrentAsyncReturnsMappedDataAndDoesNotLeakKey()
    {
        var responseJson = BuildCurrentJson();
        var captured = new RequestCapture();
        var client = CreateClient(captured, responseJson, HttpStatusCode.OK);

        var current = await client.GetCurrentAsync(new MoscowLocation(), CancellationToken.None);

        current.TemperatureC.Should().Be(20);
        current.FeelsLikeC.Should().Be(19);
        current.Condition.Text.Should().Be("Ясно");
        current.Humidity.Should().Be(50);
        captured.CapturedRelativeUrl.Should().StartWith("/v1/current.json?key=");
        captured.CapturedRelativeUrl!.Contains("?key=").Should().BeTrue("API key is required by the provider");
        captured.LastLoggedInformation.Should().NotContain("key=", "API key must never appear in logs");
    }

    [Fact]
    public async Task GetCurrentAsyncThrowsInvalidProviderResponseForEmptyPayload()
    {
        var client = CreateClient(new RequestCapture(), "{\"location\":null,\"current\":null}", HttpStatusCode.OK);

        Func<Task> act = () => client.GetCurrentAsync(new MoscowLocation(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<WeatherProviderException>();
        exception.Which.Category.Should().Be(WeatherErrorCategory.InvalidProviderResponse);
    }

    [Fact]
    public async Task GetCurrentAsyncMaps5xxToProviderUnavailable()
    {
        var client = CreateClient(new RequestCapture(), "{\"error\":{\"code\":500,\"message\":\"boom\"}}", HttpStatusCode.InternalServerError);

        Func<Task> act = () => client.GetCurrentAsync(new MoscowLocation(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<WeatherProviderException>();
        exception.Which.Category.Should().Be(WeatherProviderCategoryFromStatus(HttpStatusCode.InternalServerError));
    }

    [Fact]
    public async Task GetForecastAsyncReturnsThreeDaysAndHourlyForLocalTimezone()
    {
        var json = BuildForecastJson();
        var capture = new RequestCapture();
        var client = CreateClient(capture, json, HttpStatusCode.OK);

        var forecast = await client.GetForecastAsync(new MoscowLocation(), 3, CancellationToken.None);

        forecast.Days.Should().HaveCount(3);
        forecast.Hours.Should().NotBeEmpty();
        capture.CapturedRelativeUrl!.Contains("days=3").Should().BeTrue();
    }

    [Fact]
    public async Task GetForecastAsyncThrowsWhenTooFewDays()
    {
        var json = """
        {
          "location": {"name":"Москва","localtime":"2026-08-25 14:00","tz_id":"Europe/Moscow"},
          "forecast": {"forecastday": [
            {"date":"2026-08-25","day":{"maxtemp_c":20,"mintemp_c":10,"avgtemp_c":15,"condition":{"text":"Ясно","icon":"//x.png"},"daily_chance_of_rain":10},"astro":{"sunrise":"05:00","sunset":"21:00"},"hour":[]},
            {"date":"2026-08-26","day":{"maxtemp_c":21,"mintemp_c":11,"avgtemp_c":16,"condition":{"text":"Ясно","icon":"//x.png"},"daily_chance_of_rain":10},"astro":{"sunrise":"05:00","sunset":"21:00"},"hour":[]}
          ]}
        }
        """;
        var client = CreateClient(new RequestCapture(), json, HttpStatusCode.OK);

        Func<Task> act = () => client.GetForecastAsync(new MoscowLocation(), 3, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<WeatherProviderException>();
        exception.Which.Category.Should().Be(WeatherErrorCategory.InvalidProviderResponse);
    }

    private static WeatherErrorCategory WeatherProviderCategoryFromStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)statusCode >= 500
            ? WeatherErrorCategory.ProviderUnavailable
            : WeatherErrorCategory.InvalidProviderResponse;

    private static WeatherApiClient CreateClient(RequestCapture capture, string body, HttpStatusCode status)
    {
        var handler = new CapturingHandler(capture, body, status);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.weatherapi.com/v1/")
        };
        var options = Options.Create(new WeatherApiOptions
        {
            BaseUrl = "https://api.weatherapi.com/v1/",
            ApiKey = "secret-key-123",
            Latitude = 55.7558,
            Longitude = 37.6173,
            ForecastDays = 3,
            Timeout = TimeSpan.FromSeconds(10),
        });
        var logger = new RecordingLogger<WeatherApiClient>(capture);
        return new WeatherApiClient(httpClient, options, logger);
    }

    private static string BuildCurrentJson() =>
        """
        {
          "location": {"name":"Москва","localtime":"2026-08-25 14:00","tz_id":"Europe/Moscow"},
          "current": {
            "temp_c": 20,
            "feelslike_c": 19,
            "condition": {"text":"Ясно","icon":"//cdn.example.com/current.png"},
            "humidity": 50,
            "wind_kph": 12.3,
            "wind_dir": "N",
            "pressure_mb": 755,
            "last_updated": "2026-08-25 14:00"
          }
        }
        """;

    private static string BuildForecastJson()
    {
        var forecastDays = Enumerable.Range(0, 3).Select(dayIndex =>
        {
            var dayDate = $"2026-08-{25 + dayIndex:00}";
            return new
            {
                date = dayDate,
                day = new
                {
                    maxtemp_c = 25,
                    mintemp_c = 10,
                    avgtemp_c = 17,
                    condition = new { text = "Ясно", icon = "//cdn.example.com/day.png" },
                    daily_chance_of_rain = 15
                },
                astro = new { sunrise = "05:00", sunset = "21:00" },
                hour = Enumerable.Range(0, 24).Select(hour => new
                {
                    time = $"{dayDate} {hour:00}:00",
                    temp_c = 15 + hour,
                    condition = new { text = "Ясно", icon = "//cdn.example.com/hour.png" },
                    chance_of_rain = hour,
                    is_day = 1
                })
            };
        });

        return JsonSerializer.Serialize(new
        {
            location = new { name = "Москва", localtime = "2026-08-25 14:00", tz_id = "Europe/Moscow" },
            forecast = new { forecastday = forecastDays }
        });
    }
}

internal sealed class RequestCapture
{
    public string? CapturedRelativeUrl { get; set; }

    public string? LastLoggedInformation { get; set; }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly RequestCapture capture;

    public RecordingLogger(RequestCapture capture)
    {
        this.capture = capture;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        capture.LastLoggedInformation = formatter(state, exception);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

internal sealed class CapturingHandler(RequestCapture capture, string responseBody, HttpStatusCode status) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        capture.CapturedRelativeUrl = request.RequestUri!.IsAbsoluteUri ? request.RequestUri.PathAndQuery : request.RequestUri.OriginalString;
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
