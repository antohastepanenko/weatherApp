using System.Net.Http.Headers;
using WeatherApp.Web.Components;
using WeatherApp.Web.Services;

// Точка входа Blazor Interactive Server клиента.
// Состав:
//   1) Регистрация Razor-компонентов и интерактивного серверного render-режима.
//   2) Регистрация IWeatherBackendClient — единая точка обращения к backend.
//   3) Конвейер middleware: HSTS, маршрутизация статусов, антифоржери, статические файлы.

var builder = WebApplication.CreateBuilder(args);

// Базовый URL backend. По умолчанию — dev-профиль WeatherApp.Api.
var backendBaseUrl = builder.Configuration["Backend:BaseUrl"] ?? "https://localhost:5080/";

// Регистрация интерактивного серверного Blazor.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// HTTP-клиент к backend. Прямые вызовы WeatherAPI из Web запрещены архитектурой.
// Таймаут 15 секунд: при недоступном backend запрос не должен висеть минуту.
builder.Services.AddHttpClient<IWeatherBackendClient, WeatherBackendClient>(client =>
{
    client.BaseAddress = new Uri(backendBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    // Production: строгая политика безопасности и общий обработчик ошибок.
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Перенаправление на /not-found для несуществующих маршрутов.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();