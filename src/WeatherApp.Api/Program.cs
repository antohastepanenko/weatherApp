using MediatR;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using WeatherApp.Api;
using WeatherApp.Application;
using WeatherApp.Infrastructure;

// Точка входа backend-приложения WeatherApp.
// Состав:
//   1) Регистрация сервисов (ProblemDetails, ExceptionHandler, Application, Infrastructure, HealthChecks, CORS, OpenAPI).
//   2) Сборка конвейера middleware.
//   3) Маппинг эндпоинтов: /health и /api/weather.
// Документ OpenAPI отдаётся по /openapi/v1.json только в Development.

var builder = WebApplication.CreateBuilder(args);

// Регистрация обработчика исключений (см. WeatherExceptionHandler) и генератора ProblemDetails.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<WeatherExceptionHandler>();

// Подключение слоёв Application и Infrastructure (MediatR + WeatherAPI HTTP-клиент).
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Health-check — liveness/readiness endpoint.
builder.Services.AddHealthChecks();

// CORS только для Blazor-Web в режиме разработки.
builder.Services.AddCors(options => options.AddPolicy("WebDevelopment", policy =>
{
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["https://localhost:7080"])
        .AllowAnyHeader()
        .AllowAnyMethod();
}));

// Регистрация генератора OpenAPI 3.0 с метаданными документа.
// Используется встроенный Microsoft.AspNetCore.OpenApi (без Swashbuckle/UI).
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "WeatherApp API",
            Version = "v1",
            Description = "Backend для приложения погоды в Москве. Отдаёт снимок текущей погоды и трёхдневный прогноз для фиксированных координат 55.7558, 37.6173."
        };
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Глобальный обработчик исключений (приоритетно к другим middleware).
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    // CORS, OpenAPI-документ и Scalar UI — только в dev. На проде ни один из этих эндпоинтов не отдаётся.
    app.UseCors("WebDevelopment");
    app.MapOpenApi();

    // Документация API для разработчиков. Открывается на /scalar и тянет документ /openapi/v1.json.
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("WeatherApp API")
            .WithTheme(ScalarTheme.Kepler)
            .ExpandAllTags();
    });
}
app.UseHttpsRedirection();
app.MapHealthChecks("/health");

// Эндпоинт погоды: отдаёт WeatherResponse для Москвы.
// Возможные коды ответа:
//   200 — успех (WeatherResponse),
//   502 — провайдер недоступен или вернул 5xx/429,
//   504 — таймаут провайдера.
app.MapGet("/api/weather", async (ISender sender, CancellationToken cancellationToken) =>
    Results.Ok(await sender.Send(new GetMoscowWeatherQuery(), cancellationToken)))
    .WithName("GetMoscowWeather")
    .WithSummary("Текущая погода и прогноз в Москве")
    .WithDescription("Возвращает текущие погодные условия и трёхдневный прогноз для фиксированных координат Москвы (55.7558, 37.6173). Почасовой прогноз содержит оставшиеся часы текущего дня и все часы следующего календарного дня.")
    .Produces<WeatherApp.Contracts.WeatherResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status504GatewayTimeout);

app.Run();

// Маркер для WebApplicationFactory в integration-тестах.
public partial class Program;
