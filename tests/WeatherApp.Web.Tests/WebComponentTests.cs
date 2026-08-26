using System.Net;
using System.Net.Http;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using WeatherApp.Contracts;
using WeatherApp.Web.Components.Pages;
using WeatherApp.Web.Components.Shared;
using WeatherApp.Web.Components.Weather;
using WeatherApp.Web.Services;

namespace WeatherApp.Web.Tests;

public sealed class CurrentWeatherCardTests : BunitContext
{
    [Fact]
    public void RendersTemperatureAndFacts()
    {
        var weather = new CurrentWeather(
            TemperatureC: 21,
            FeelsLikeC: 19.4,
            ConditionText: "Ясно",
            IconUrl: "https://cdn.example.com/icon.png",
            Humidity: 50,
            WindKph: 12.3,
            WindDirection: "N",
            PressureMb: 755,
            UpdatedAt: new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero));

        var cut = Render<CurrentWeatherCard>(parameters => parameters.Add(p => p.Weather, weather));

        cut.Markup.Should().Contain("Ясно");
        cut.Markup.Should().Contain("21");
        cut.Markup.Should().Contain("Ощущается как 19");
        cut.Markup.Should().Contain("Влажность");
        cut.Markup.Should().Contain("Ветер");
        cut.Markup.Should().Contain("Давление");
    }

    [Fact]
    public void FallsBackToEmDashForMissingValues()
    {
        var weather = new CurrentWeather(
            TemperatureC: null,
            FeelsLikeC: null,
            ConditionText: null,
            IconUrl: null,
            Humidity: null,
            WindKph: null,
            WindDirection: null,
            PressureMb: null,
            UpdatedAt: null);

        var cut = Render<CurrentWeatherCard>(parameters => parameters.Add(p => p.Weather, weather));

        cut.Markup.Should().Contain("—");
        cut.Markup.Should().NotContain("null");
    }
}

public sealed class HourlyForecastTests : BunitContext
{
    [Fact]
    public void RendersAllProvidedHours()
    {
        var hours = new List<HourlyWeather>
        {
            new(new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero), 21, "Ясно", null, 10, true),
            new(new DateTimeOffset(2026, 8, 25, 15, 0, 0, TimeSpan.Zero), 22, "Облачно", null, 20, true),
            new(new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero), 23, "Ясно", null, 0, false),
        };

        var cut = Render<HourlyForecast>(parameters => parameters.Add(p => p.Hours, hours));

        cut.Markup.Should().Contain("Почасовой прогноз");
        cut.FindAll(".hour-card").Should().HaveCount(3);
    }
}

public sealed class DailyForecastTests : BunitContext
{
    [Fact]
    public void RendersThreeDayCards()
    {
        var days = new List<DailyWeather>
        {
            new(new DateOnly(2026, 8, 25), 10, 25, 18, "Ясно", null, 0, new TimeOnly(5, 0), new TimeOnly(21, 0)),
            new(new DateOnly(2026, 8, 26), 11, 26, 19, "Облачно", null, 30, new TimeOnly(5, 1), new TimeOnly(21, 1)),
            new(new DateOnly(2026, 8, 27), 12, 27, 20, "Дождь", null, 80, new TimeOnly(5, 2), new TimeOnly(21, 2)),
        };

        var cut = Render<DailyForecast>(parameters => parameters.Add(p => p.Days, days));

        cut.FindAll(".day-card").Should().HaveCount(3);
        cut.Markup.Should().Contain("Восход");
        cut.Markup.Should().Contain("закат");
    }
}

public sealed class LoadingStateTests : BunitContext
{
    [Fact]
    public void RendersLoadingSpinner()
    {
        var cut = Render<LoadingState>();

        cut.Markup.Should().Contain("loading-spinner");
        cut.Markup.Should().Contain("Загружаем погоду");
        cut.Find(".loading-state").Should().NotBeNull();
    }
}

public sealed class ErrorStateTests : BunitContext
{
    [Fact]
    public void RendersMessageAndRetryButton()
    {
        var clicked = 0;
        var cut = Render<ErrorState>(parameters => parameters
            .Add(p => p.Message, "Сервис недоступен")
            .Add(p => p.IsRetrying, false)
            .Add(p => p.OnRetry, EventCallback.Factory.Create(this, () => clicked++)));

        cut.Markup.Should().Contain("Сервис недоступен");
        cut.Find("button.retry-button").TextContent.Should().Contain("Повторить");

        cut.Find("button.retry-button").Click();

        clicked.Should().Be(1);
    }

    [Fact]
    public void DisablesRetryWhileRetrying()
    {
        var cut = Render<ErrorState>(parameters => parameters
            .Add(p => p.Message, "Ошибка")
            .Add(p => p.IsRetrying, true)
            .Add(p => p.OnRetry, EventCallback.Empty));

        var button = cut.Find("button.retry-button");
        button.HasAttribute("disabled").Should().BeTrue();
        button.TextContent.Should().Contain("Повторяем");
    }
}

public sealed class WeatherBackendClientTests
{
    [Fact]
    public async Task SendsRequestToConfiguredBackend()
    {
        var response = new WeatherResponse(
            new WeatherLocation("Москва", new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero), "Europe/Moscow"),
            new CurrentWeather(21, 19, "Ясно", null, 50, 12, "N", 755, new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero)),
            [new HourlyWeather(new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero), 21, "Ясно", null, 0, true)],
            [new DailyWeather(new DateOnly(2026, 8, 25), 10, 25, 18, "Ясно", null, 0, new TimeOnly(5, 0), new TimeOnly(21, 0))]);
        var handler = new CapturingHandler("/api/weather", response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:5080/") };
        var sut = new WeatherBackendClient(client);

        var actual = await sut.GetWeatherAsync(CancellationToken.None);

        handler.LastPath.Should().Be("/api/weather");
        actual.Current.ConditionText.Should().Be("Ясно");
    }

    [Fact]
    public async Task ThrowsFriendlyExceptionForGatewayTimeout()
    {
        var handler = new CapturingHandler("/api/weather", HttpStatusCode.GatewayTimeout);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:5080/") };
        var sut = new WeatherBackendClient(client);

        Func<Task> act = () => sut.GetWeatherAsync(CancellationToken.None);

        var exception = await act.Should().ThrowAsync<WeatherBackendException>();
        exception.Which.Message.Should().Contain("слишком долго");
    }

    [Fact]
    public async Task ThrowsFriendlyExceptionForUnavailableProvider()
    {
        var handler = new CapturingHandler("/api/weather", HttpStatusCode.BadGateway);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:5080/") };
        var sut = new WeatherBackendClient(client);

        Func<Task> act = () => sut.GetWeatherAsync(CancellationToken.None);

        var exception = await act.Should().ThrowAsync<WeatherBackendException>();
        exception.Which.Message.Should().Contain("недоступен");
    }

    private sealed class CapturingHandler(string expectedPath, WeatherResponse payload) : HttpMessageHandler
    {
        private readonly HttpStatusCode? failureStatus;

        public CapturingHandler(string expectedPath, HttpStatusCode status)
            : this(expectedPath, payload: null!)
        {
            failureStatus = status;
        }

        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri!.AbsolutePath;
            LastPath.Should().Be(expectedPath);
            if (failureStatus.HasValue)
            {
                return Task.FromResult(new HttpResponseMessage(failureStatus.Value));
            }
            var json = JsonSerializer.Serialize(payload);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}

public sealed class WeatherPageTests : BunitContext
{
    [Fact]
    public void RendersLoadingStateBeforeDataArrives()
    {
        var client = Substitute.For<IWeatherBackendClient>();
        client.GetWeatherAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return null!;
        });
        Services.AddSingleton(client);

        var cut = Render<Weather>();

        cut.WaitForState(() => cut.Markup.Contains("loading-state"), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RendersAllThreeSectionsAfterSuccess()
    {
        var response = new WeatherResponse(
            new WeatherLocation("Москва", new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero), "Europe/Moscow"),
            new CurrentWeather(21, 19, "Ясно", null, 50, 12, "N", 755, new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero)),
            [new HourlyWeather(new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero), 21, "Ясно", null, 0, true)],
            [
                new DailyWeather(new DateOnly(2026, 8, 25), 10, 25, 18, "Ясно", null, 0, new TimeOnly(5, 0), new TimeOnly(21, 0)),
                new DailyWeather(new DateOnly(2026, 8, 26), 11, 26, 19, "Облачно", null, 30, new TimeOnly(5, 1), new TimeOnly(21, 1)),
                new DailyWeather(new DateOnly(2026, 8, 27), 12, 27, 20, "Дождь", null, 80, new TimeOnly(5, 2), new TimeOnly(21, 2)),
            ]);
        var client = Substitute.For<IWeatherBackendClient>();
        client.GetWeatherAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(response));
        Services.AddSingleton(client);

        var cut = Render<Weather>();

        cut.WaitForState(() => cut.Markup.Contains("current-card") && cut.Markup.Contains("Почасовой прогноз") && cut.Markup.Contains("Прогноз на 3 дня"), TimeSpan.FromSeconds(2));
        cut.Find(".current-card").Should().NotBeNull();
        cut.Find(".hourly-list").Should().NotBeNull();
        cut.Find(".daily-grid").Should().NotBeNull();
    }

    [Fact]
    public void RendersErrorStateWithRetryButtonOnFailure()
    {
        var client = Substitute.For<IWeatherBackendClient>();
        client.GetWeatherAsync(Arg.Any<CancellationToken>()).Returns<Task<WeatherResponse>>(_ => throw new WeatherBackendException("Backend недоступен"));
        Services.AddSingleton(client);

        var cut = Render<Weather>();

        cut.WaitForState(() => cut.Markup.Contains("error-state"), TimeSpan.FromSeconds(2));
        cut.Find("button.retry-button").Should().NotBeNull();
    }

    [Fact]
    public async Task RetryAfterHttpRequestExceptionTriggersNewRequest()
    {
        var calls = 0;
        var client = Substitute.For<IWeatherBackendClient>();
        var response = new WeatherResponse(
            new WeatherLocation("Москва", new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero), "Europe/Moscow"),
            new CurrentWeather(21, 19, "Ясно", null, 50, 12, "N", 755, new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero)),
            [new HourlyWeather(new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero), 21, "Ясно", null, 0, true)],
            [new DailyWeather(new DateOnly(2026, 8, 25), 10, 25, 18, "Ясно", null, 0, new TimeOnly(5, 0), new TimeOnly(21, 0))]);
        client.GetWeatherAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls++;
            return calls == 1
                ? throw new HttpRequestException("Connection refused")
                : Task.FromResult(response);
        });
        Services.AddSingleton(client);

        var cut = Render<Weather>();

        cut.WaitForState(() => cut.Markup.Contains("error-state"), TimeSpan.FromSeconds(2));
        calls.Should().Be(1);

        cut.Find("button.retry-button").Click();

        cut.WaitForState(() => cut.Markup.Contains("current-card"), TimeSpan.FromSeconds(2));
        calls.Should().Be(2, "retry must issue a new backend call");
    }

    [Fact]
    public async Task TaskCanceledExceptionShowsTimeoutMessage()
    {
        // На .NET resilience-pipeline при недоступном backend HttpClient может бросить
        // TaskCanceledException вместо HttpRequestException — например, после retry.
        var client = Substitute.For<IWeatherBackendClient>();
        client.GetWeatherAsync(Arg.Any<CancellationToken>()).Returns<Task<WeatherResponse>>(_ => throw new TaskCanceledException("Timeout"));
        Services.AddSingleton(client);

        var cut = Render<Weather>();

        cut.WaitForState(() => cut.Markup.Contains("Сервер не отвечает"), TimeSpan.FromSeconds(2));
        cut.Find("button.retry-button").Should().NotBeNull();
        // Retry-кнопка должна быть активна после отказа (isLoading сброшен в finally).
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.WaitHandle.WaitOne();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class EmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}