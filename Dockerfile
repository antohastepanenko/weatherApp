# syntax=docker/dockerfile:1.7

# ----------------------------------------------------------------------------
# Базовый этап: restore + publish API и Web в один образ.
# Финальные этапы api/web ниже выбирают, какой из артефактов запускать.
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/WeatherApp.Domain/WeatherApp.Domain.csproj           src/WeatherApp.Domain/
COPY src/WeatherApp.Contracts/WeatherApp.Contracts.csproj     src/WeatherApp.Contracts/
COPY src/WeatherApp.Application/WeatherApp.Application.csproj src/WeatherApp.Application/
COPY src/WeatherApp.Infrastructure/WeatherApp.Infrastructure.csproj src/WeatherApp.Infrastructure/
COPY src/WeatherApp.Api/WeatherApp.Api.csproj                 src/WeatherApp.Api/
COPY src/WeatherApp.Web/WeatherApp.Web.csproj                 src/WeatherApp.Web/

RUN dotnet restore src/WeatherApp.Api/WeatherApp.Api.csproj
RUN dotnet restore src/WeatherApp.Web/WeatherApp.Web.csproj

# Копируем остальной исходный код.
COPY src/ src/

# build API — без static web assets, без особенностей.
RUN dotnet build src/WeatherApp.Api/WeatherApp.Api.csproj \
        -c $BUILD_CONFIGURATION --no-restore
# build Web. Генерирует staticwebassets.build.json, который нужен publish-шагу
# ниже. Сам по себе этот шаг не публикует фреймворк-ассеты — это делает
# последующий dotnet publish (без --no-build) для Web.
RUN dotnet build src/WeatherApp.Web/WeatherApp.Web.csproj \
        -c $BUILD_CONFIGURATION --no-restore

# Публикуем API (--no-build допустим: у него нет static web assets).
RUN dotnet publish src/WeatherApp.Api/WeatherApp.Api.csproj \
        -c $BUILD_CONFIGURATION -o /app/publish/api --no-build /p:UseAppHost=false
# Публикуем Web БЕЗ --no-build. Это принудительно пересобирает проект и
# гарантирует, что MSBuild подтянет фреймворк-ассеты
# Microsoft.AspNetCore.App.Internal.Assets (включая _framework/blazor.web.js)
# в wwwroot/_framework/, а endpoints-манифест будет содержать маршруты
# для MapStaticAssets. Без этого (т.е. с --no-build) контейнер стартует, но
# в браузере 404 на _framework/blazor.web.js, интерактивный рендеринг ломается,
# стили не применяются.
RUN dotnet publish src/WeatherApp.Web/WeatherApp.Web.csproj \
        -c $BUILD_CONFIGURATION -o /app/publish/web /p:UseAppHost=false

# ----------------------------------------------------------------------------
# Общий финальный слой с пользователем app и точкой монтирования /app.
# Конкретный сервис выбирается через target: --target api или --target web.
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Базовый образ aspnet:10.0 уже содержит непривилегированного пользователя app (uid/gid 1000)
# с домашним каталогом /app, поэтому отдельный groupadd/useradd не нужен.

WORKDIR /app

# Каталоги логов под пользователем app.
RUN mkdir -p /app/logs && chown -R app:app /app

ENV \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true

EXPOSE 8080 8081

USER app

# ----------------------------------------------------------------------------
# Этап api: запускает только WeatherApp.Api на 8080.
# ----------------------------------------------------------------------------
FROM runtime AS api
COPY --from=build --chown=app:app /app/publish/api/ /app/
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/WeatherApp.Api.dll"]

# ----------------------------------------------------------------------------
# Этап web: запускает только WeatherApp.Web на 8081.
# ----------------------------------------------------------------------------
FROM runtime AS web
COPY --from=build --chown=app:app /app/publish/web/ /app/
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081
ENTRYPOINT ["dotnet", "/app/WeatherApp.Web.dll"]
