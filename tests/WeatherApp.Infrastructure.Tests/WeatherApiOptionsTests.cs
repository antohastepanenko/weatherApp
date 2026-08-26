using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WeatherApp.Infrastructure;

namespace WeatherApp.Infrastructure.Tests;

public sealed class WeatherApiOptionsTests
{
    [Fact]
    public void ValidOptionsPassValidation()
    {
        var options = new WeatherApiOptions
        {
            BaseUrl = "https://api.weatherapi.com/v1/",
            ApiKey = "abc",
        };

        Validator.TryValidateObject(options, new ValidationContext(options), null, validateAllProperties: true).Should().BeTrue();
        options.Validate(new ValidationContext(options)).Should().BeEmpty();
    }

    [Fact]
    public void OptionsRejectNonMoscowCoordinates()
    {
        var options = new WeatherApiOptions { Latitude = 0, Longitude = 0 };
        var results = options.Validate(new ValidationContext(options)).ToList();

        results.Should().Contain(r => r.MemberNames.Contains(nameof(WeatherApiOptions.Latitude)));
    }

    [Fact]
    public void OptionsRejectWrongForecastDays()
    {
        var options = new WeatherApiOptions { ForecastDays = 5 };
        var results = options.Validate(new ValidationContext(options)).ToList();

        results.Should().Contain(r => r.MemberNames.Contains(nameof(WeatherApiOptions.ForecastDays)));
    }

    [Fact]
    public void OptionsRejectLongTimeout()
    {
        var options = new WeatherApiOptions { Timeout = TimeSpan.FromMinutes(2) };
        var results = options.Validate(new ValidationContext(options)).ToList();

        results.Should().Contain(r => r.MemberNames.Contains(nameof(WeatherApiOptions.Timeout)));
    }

    [Fact]
    public void OptionsRejectNonHttpsBaseUrl()
    {
        var options = new WeatherApiOptions { BaseUrl = "http://api.weatherapi.com/v1/" };
        var results = options.Validate(new ValidationContext(options)).ToList();

        results.Should().Contain(r => r.MemberNames.Contains(nameof(WeatherApiOptions.BaseUrl)));
    }

    [Fact]
    public void OptionsAllowLocalhostForTests()
    {
        var options = new WeatherApiOptions { BaseUrl = "http://localhost:5080/" };
        var results = options.Validate(new ValidationContext(options)).ToList();

        results.Should().BeEmpty();
    }
}