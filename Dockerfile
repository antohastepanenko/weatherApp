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

# Сначала выполняем build (без --no-restore), чтобы MSBuild сгенерировал
# staticwebassets.build.json для Blazor-проекта. Без этого publish для Web
# не подхватит фреймворк-ассеты Microsoft.AspNetCore.App.Internal.Assets
# (в частности, _framework/blazor.web.js), и Blazor не сможет загрузиться.
RUN dotnet build src/WeatherApp.Api/WeatherApp.Api.csproj \
        -c $BUILD_CONFIGURATION --no-restore
RUN dotnet build src/WeatherApp.Web/WeatherApp.Web.csproj \
        -c $BUILD_CONFIGURATION --no-restore

# Публикуем оба приложения.
RUN dotnet publish src/WeatherApp.Api/WeatherApp.Api.csproj \
        -c $BUILD_CONFIGURATION -o /app/publish/api --no-build /p:UseAppHost=false
RUN dotnet publish src/WeatherApp.Web/WeatherApp.Web.csproj \
        -c $BUILD_CONFIGURATION -o /app/publish/web --no-build /p:UseAppHost=false

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
COPY --from=build --chown=app:app /app/publish/api /app/app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/app/WeatherApp.Api.dll"]

# ----------------------------------------------------------------------------
# Этап web: запускает только WeatherApp.Web на 8081.
# ----------------------------------------------------------------------------
FROM runtime AS web
COPY --from=build --chown=app:app /app/publish/web /app/app
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081
ENTRYPOINT ["dotnet", "/app/app/WeatherApp.Web.dll"]
