# WeatherApp

WeatherApp — погодное приложение для фиксированной точки Москвы (`55.7558, 37.6173`) на .NET 10. Backend изолирует ключ WeatherAPI.com и реализует единственный публичный endpoint `GET /api/weather`; Blazor Web App обращается только к нему и не знает деталей внешнего провайдера.

## Архитектура

Решение построено по принципам Clean Architecture: зависимости направлены строго внутрь, внешние детали изолированы на границах слоёв.

- `src/WeatherApp.Domain` — провайдеро-независимая модель погоды, фиксированная `MoscowLocation`, нормализация иконок, выборка часов прогноза, маппинг в публичную DTO.
- `src/WeatherApp.Contracts` — DTO, общие для API и Blazor-клиента.
- `src/WeatherApp.Application` — MediatR-запрос `GetMoscowWeatherQuery`, его обработчик, порт `IWeatherProvider`, категории ошибок и поведение логирования use-case. Handler параллельно запускает обращения к провайдеру за текущей погодой и прогнозом и склеивает результат.
- `src/WeatherApp.Infrastructure` — реализация `IWeatherProvider` через типизированный `WeatherApiClient`: JSON DTO, валидация options, построение HTTP-запроса, маппинг статусов и ошибок, стандартный resilience handler.
- `src/WeatherApp.Api` — composition root backend. `Program.cs` регистрирует слои Application/Infrastructure, exception handler, CORS для Development, OpenAPI, health checks и endpoint `/api/weather`.
- `src/WeatherApp.Web` — отдельный интерактивный Blazor Server клиент. Ходит к backend только через `Services/WeatherBackendClient`; `Components/Pages/Weather.razor` владеет состоянием загрузки/успеха/ошибки/ретрая/отмены, дочерние компоненты рендерят текущую погоду, почасовой и дневной прогноз.

Жёсткие правила, заложенные в архитектуру:

- `Domain` и `Contracts` независимы от ASP.NET Core и деталей провайдера.
- `Application` не ссылается на `Infrastructure` или `Api`.
- `Web` зависит только от `Contracts` и backend HTTP, но не от WeatherAPI напрямую.
- Внешние DTO WeatherAPI живут в `Infrastructure` и маппятся в доменные типы до возврата наружу.
- Почасовой прогноз выбирается по Москве: оставшиеся часы текущего дня, начиная с текущего локального часа, затем все часы следующего календарного дня, в хронологическом порядке. Серверные часы и пользовательские координаты не используются.
- Ошибки провайдера превращаются в безопасные Problem Details: ключи, полные query-URL, стектрейсы и внутренности провайдера наружу не уходят.
- Ретраи разрешены только для транзиентных сбоев (timeout, 408, 429, 5xx) через resilience handler; неограниченные ретраи и прямые вызовы провайдера из браузера запрещены.

## Технологический стек

- .NET 10 (`net10.0`), nullable reference types, implicit usings, preview language features.
- MediatR 14 для use-case.
- `Microsoft.Extensions.Http.Resilience` для повторов и таймаутов.
- `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` для OpenAPI 3.0 и UI в Development.
- bUnit, xUnit, NSubstitute, FluentAssertions, `Microsoft.AspNetCore.Mvc.Testing` для тестов.
- Версии пакетов централизованы в `Directory.Packages.props`; общие свойства сборки — в `Directory.Build.props` (`TreatWarningsAsErrors=true`).

## Структура репозитория

```
.
├── src/
│   ├── WeatherApp.Domain/
│   ├── WeatherApp.Contracts/
│   ├── WeatherApp.Application/
│   ├── WeatherApp.Infrastructure/
│   ├── WeatherApp.Api/                # backend composition root
│   └── WeatherApp.Web/                # Blazor Interactive Server client
├── tests/                             # тесты-проекты по слоям (пока без исходников)
├── Dockerfile                         # multi-stage: target api и target web
├── docker-compose.yml                 # сервисы api (8080) и web (8081)
├── Directory.Build.props
├── Directory.Packages.props
├── WeatherApp.slnx
├── .env.example                       # шаблон ключа для локальной переменной
└── README.md
```

## Конфигурация и секреты

Реальный API key WeatherAPI никогда не хранится в репозитории. Любой способ ниже эквивалентен по смыслу, выберите удобный:

- User Secrets для проекта API:
  ```bash
  dotnet user-secrets --project src/WeatherApp.Api set WeatherApi:ApiKey "<your-key>"
  ```
- Переменная окружения в текущей оболочке:
  ```bash
  export WeatherApi__ApiKey="<your-key>"
  ```
- Локальный файл `.env` рядом с `docker-compose.yml` (используется `docker compose` через `WEATHERAPI_KEY`; шаблон `.env.example.compose` намеренно удалён, чтобы случайно не закоммитить ключ).

Если значение когда-либо публиковалось, считайте его скомпрометированным и выпустите новый ключ в кабинете WeatherAPI. Текущее значение ключа в репозитории отсутствует: `WeatherApiOptions.ApiKey` имеет пустой дефолт, а валидация `[Required, MinLength(1)]` требует реального значения в конфигурации на старте.

Прочие настройки, которые использует API:

- `WeatherApi:BaseUrl` — по умолчанию `https://api.weatherapi.com/v1/`.
- `WeatherApi:Latitude` / `WeatherApi:Longitude` — фиксированные координаты Москвы.
- `WeatherApi:ForecastDays` — количество дней прогноза (по умолчанию `3`).
- `WeatherApi:Timeout` — таймаут HTTP-запроса к провайдеру.
- `Cors:AllowedOrigins` — список origin, которым разрешено ходить к API в Development (по умолчанию `https://localhost:7080`, `http://localhost:5081`).
- `Backend:BaseUrl` для Web — базовый URL backend, по умолчанию `https://localhost:5080/`.

## Запуск на локальной машине

### Требования

- .NET SDK 10.
- Активный API key WeatherAPI.
- Два свободных порта: `5080`/`5081` для API и `7080`/`7081` для Web (значения по умолчанию в launch profiles).

### Шаги

1. Восстановите зависимости и соберите решение:
   ```bash
   dotnet restore WeatherApp.slnx
   dotnet build WeatherApp.slnx
   ```
2. Задайте секрет любым удобным способом (см. раздел про секреты).
3. В первом терминале запустите backend:
   ```bash
   dotnet run --project src/WeatherApp.Api --launch-profile WeatherApp.Api
   ```
   API поднимется на `https://localhost:5080` (HTTP — `5081`), `/health` доступен сразу.
4. Во втором терминале запустите Web:
   ```bash
   dotnet run --project src/WeatherApp.Web --launch-profile WeatherApp.Web
   ```
   Blazor откроется на `https://localhost:7080` (HTTP — `7081`).

Откройте `https://localhost:7080` — UI дёрнет `GET https://localhost:5080/api/weather` и отрисует текущую погоду, почасовой и трёхдневный прогноз. OpenAPI документ отдаётся только в Development: `/openapi/v1.json`, Scalar UI — `/scalar`.

## Проверка

```bash
dotnet restore WeatherApp.slnx
dotnet build WeatherApp.slnx
dotnet test WeatherApp.slnx --no-restore
```

Запуск тестов одного слоя, например для селектора часов:

```bash
dotnet test tests/WeatherApp.Domain.Tests/WeatherApp.Domain.Tests.csproj --no-restore
dotnet test tests/WeatherApp.Domain.Tests/WeatherApp.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~HourlyForecastSelector"
```

Тесты используют fake/HTTP handler и не обращаются к реальному WeatherAPI.

## Запуск в Docker

В репозитории есть `Dockerfile` (multi-stage с двумя целями — `api` и `web`) и `docker-compose.yml` с двумя сервисами. Каждый сервис — это отдельный контейнер из общего этапа сборки: API слушает `8080`, Blazor Web — `8081`. Внутри compose-сети Web обращается к API по имени сервиса `api`.

### Шаги

1. Подготовьте файл с секретом рядом с `docker-compose.yml` (формат: `WEATHERAPI_KEY=your-real-key`). `.gitignore` исключает `.env`, поэтому в репозиторий файл не попадёт.
2. Соберите и запустите:
   ```bash
   docker compose build
   docker compose up -d
   ```
3. Проверьте состояние:
   ```bash
   docker compose ps
   docker compose logs -f api
   docker compose logs -f web
   ```
   Healthcheck опрашивает `http://localhost:8080/health` каждые 30 секунд.
4. Откройте в браузере:
   - Blazor Web UI: `http://localhost:8081`.
   - Backend API: `http://localhost:8080/api/weather`.
   - Health check: `http://localhost:8080/health`.

### Что происходит внутри

- Сервис `api` собирается из `Dockerfile` с `--target api` и запускает только `WeatherApp.Api.dll` через `ENTRYPOINT ["dotnet", ...]` на `http://+:8080`.
- Сервис `web` собирается из `--target web`, запускает только `WeatherApp.Web.dll` на `http://+:8081` и обращается к API по `http://api:8080/` (имя сервиса в compose-сети задаёт `Backend__BaseUrl`).
- `WeatherApi__ApiKey` пробрасывается из `.env` только в сервис `api`; в образе и в `appsettings*.json` ключа нет.
- HTTPS-редирект внутри контейнеров отключён на уровне URL (контейнеры слушают только HTTP); при необходимости выставьте наружу reverse proxy с TLS, не меняя кода.

### Остановка и очистка

```bash
docker compose down            # остановить и удалить контейнеры
docker compose down --rmi all  # дополнительно удалить образы api и web
```

## Безопасность

- Не коммитьте `.env`, `appsettings.Development.json` с ключами, `weather.md` и любые production-снимки, содержащие чувствительные данные.
- API не отдаёт ключ, полный URL запроса, стектрейсы и внутренности провайдера: смотрите `WeatherExceptionHandler` и маппинг ошибок в `Infrastructure`.
- CORS-политика `WebDevelopment` активна только в Development. В Production UI и API предполагается публиковать за reverse proxy с общим origin.
