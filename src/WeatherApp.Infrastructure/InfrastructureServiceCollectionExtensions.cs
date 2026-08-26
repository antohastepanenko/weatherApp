using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using WeatherApp.Application;

namespace WeatherApp.Infrastructure;

/// <summary>
/// Расширения регистрации слоя Infrastructure в DI.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Привязывает <see cref="WeatherApiOptions"/> к конфигурации, валидирует их на старте
    /// и регистрирует <see cref="WeatherApiClient"/> как реализацию <see cref="IWeatherProvider"/>
    /// со стандартным resilience-обработчиком.
    /// </summary>
    /// <remarks>
    /// Resilience-обработчик делает ретраи только на транзакторные ошибки
    /// (<see cref="HttpRequestException"/>, <see cref="TimeoutRejectedException"/>,
    /// HTTP 408/429/5xx) и ограничивает общее время ожидания.
    /// </remarks>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="configuration">Конфигурация приложения (источник секции <c>WeatherApi</c>).</param>
    /// <returns>Та же коллекция — для цепочного вызова.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WeatherApiOptions>()
            .Bind(configuration.GetSection(WeatherApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IWeatherProvider, WeatherApiClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<WeatherApiOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(response => response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                                              response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                                              (int)response.StatusCode >= 500);
            });

        return services;
    }
}