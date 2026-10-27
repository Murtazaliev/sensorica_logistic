# DeliverySystem — исходный код

> Текущий этап: **мок amoCRM + офлайн-синк + UI диспетчера + 2ГИС + маршруты** (см. `../30-plan/current-state.md`).
> Спецификация: `../10-spec/full-spec.md`. Решения: `../20-decisions/`.

## Стек

- .NET 10 (`net10.0`)
- ASP.NET Core Minimal API
- Blazor Server (интерактивный UI в том же хосте)
- Cookie-аутентификация + роли (ASP.NET Core, `PasswordHasher`)
- 2ГИС: MapGL JS API + Geocoder/Suggest API
- EF Core 10 + SQLite (временная БД; целевая — PostgreSQL + PostGIS)
- Swashbuckle 10 (Swagger UI, только Development)
- xUnit

## Структура решения

```text
src/
├── DeliverySystem.slnx
├── DeliverySystem.Core/            # домен, контракты приёма и синка
│   ├── Domain/                     # сущности, enum'ы, AppUser, роли
│   ├── Intake/                     # LeadEvent, IOrderIntakeSource
│   ├── Maps/                       # GeocodePlace, AddressSuggestion
│   ├── Points/                     # PointUpsertRequest/Result
│   ├── Routes/                     # Courier/Route DTO, запросы остановок
│   └── Sync/                       # ISyncService, DTO, SyncJson
├── DeliverySystem.Infrastructure/  # EF Core, intake, sync, точки, маршруты, 2ГИС
│   ├── Maps/TwoGisGeocoder.cs      # Geocoder/Suggest API 2ГИС
│   ├── Maps/TwoGisRouter.cs        # Directions API 2ГИС (дорожная геометрия)
│   ├── Maps/TwoGisDistanceMatrix.cs # Distance Matrix API (авто-порядок)
│   ├── Routes/RouteService.cs      # черновики, остановки, авто-порядок
│   ├── Demo/DemoOrderService.cs    # демо-заказы для маршрутов
│   └── Fixtures/amo/*.json         # снимки payload'ов amoCRM (мок)
├── DeliverySystem.Api/             # Minimal API + Swagger + Blazor UI
│   └── wwwroot/                    # map.js, route-map.js, icons/, app.css
└── DeliverySystem.Tests/           # 30 тестов: intake, sync, точки, маршруты, геокодер
```

## Запуск

```powershell
dotnet run --project .\DeliverySystem.Api
```

По умолчанию: `http://localhost:5202` (см. `Properties/launchSettings.json`), БД `delivery.db`.
Вход: `http://localhost:5202/login` (без входа страницы перенаправляют сюда).
Swagger UI: `http://localhost:5202/swagger`.
UI диспетчера: `http://localhost:5202/orders`, `/routes`, `/points` и `/sync`.

Демо-учётки (Development, пароль `demo123`):

| Email | Роль |
|---|---|
| `admin@demo.local` | Администратор |
| `dispatcher@demo.local` | Диспетчер |
| `head@demo.local` | Руководитель |
| `courier@demo.local` | Курьер (автомобиль) |
| `courier2@demo.local` | Курьер 2 (мотоцикл) |
| `courier3@demo.local` | Курьер 3 (пешком) |
| `point@demo.local` | Менеджер точки |
| `sales@demo.local` | Менеджер продаж |

Ключ 2ГИС (MapGL, Geocoder, Suggest) хранится в **.NET user secrets** и не попадает в репозиторий:

```powershell
dotnet user-secrets set "TwoGis:ApiKey" "<ключ из кабинета 2ГИС>" --project .\DeliverySystem.Api
```

User secrets подхватываются в Development. В production используйте переменную окружения `TwoGis__ApiKey` или Vault.

Адрес и координаты цеха — в `appsettings.json`, секция `Depot` (правится без пересборки):

```json
"Depot": {
  "Name": "Цех",
  "Address": "Москва, ул. Тверская, 1",
  "Latitude": 55.7576,
  "Longitude": 37.6054
}
```

> `EnsureCreated` не обновляет существующую схему. Для dev-таблиц (`Users`, `Couriers`, `Routes`, `RoutePoints`) и координат точек в `DevSeed` есть автодобавление; для произвольных изменений схемы миграций пока нет — при необходимости удалите `DeliverySystem.Api\delivery.db*`.

## Эндпоинты

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/` | Список ключевых путей |
| GET | `/health` | Проверка живости |
| GET | `/login` | Страница входа (без авторизации) |
| POST | `/auth/login` | Вход по email/паролю, cookie-сессия |
| GET | `/auth/logout` | Выход |
| GET | `/welcome` | Кабинет роли (все авторизованные) |
| GET | `/forbidden` | 403 при недостатке прав |
| GET | `/swagger` | Swagger UI (только Development) |
| GET | `/orders` | UI: список заказов, фильтры, статистика |
| GET | `/orders/{id}` | UI: карточка, история, смена статуса |
| GET | `/routes` | UI: планировщик маршрутов (дата, курьер, остановки, карта) |
| GET | `/points` | UI: точки доставки на карте 2ГИС, создание и модерация |
| GET | `/sync` | UI: change feed, журнал операций, dev-симуляция |
| GET | `/api/orders` | Список заказов |
| GET | `/api/orders/{id}` | Заказ с позициями и историей |
| GET | `/api/points` | Список точек доставки с координатами |
| POST | `/api/points` | Создать или обновить точку (и подтвердить модерацию) |
| GET | `/api/couriers` | Курьеры (3 демо) |
| GET | `/api/routes?date=...` | Маршруты на дату |
| GET | `/api/routes/{id}` | Маршрут с остановками |
| POST | `/api/routes` | Создать/получить черновик на курьера и дату |
| PUT | `/api/routes/{id}/stops` | Заменить остановки (порядок = порядок id) |
| POST | `/api/routes/{id}/auto-order` | Авто-порядок по прямой (ближайший сосед) |
| POST | `/api/routes/{id}/confirm` | Подтвердить и заблокировать маршрут |
| GET | `/api/routes/available-orders?date=...` | Свободные заказы на дату |
| POST | `/dev/orders/demo?date=...` | Демо-заказы для маршрута (только Development) |
| GET | `/api/geocode?q=...` | Координаты по адресу (Geocoder API 2ГИС) |
| GET | `/api/geocode/reverse?lat=...&lng=...` | Адрес по координатам |
| GET | `/api/suggest?q=...` | Подсказки адресов (Suggest API 2ГИС) |
| GET | `/api/webhook-logs` | Журнал вебхуков |
| POST | `/api/sync/push` | Идемпотентный приём операций курьера |
| GET | `/api/sync/pull?since=0&limit=100` | Delta pull изменений + tombstones |
| GET | `/dev/amo/scenarios` | Список фикстур (только Development) |
| POST | `/dev/amo/simulate` | Проиграть сценарий amoCRM (только Development) |

## Доступ по ролям

- `Admin`, `Dispatcher`, `Head` — заказы, карточка, маршруты, точки, модерация.
- `Admin`, `Dispatcher` — лента синка и симуляция amoCRM.
- `Admin` — журнал вебхуков.
- Любой авторизованный — sync pull/push, список точек, геокодинг, `/welcome`.
- `Courier`, `PointManager`, `SalesManager` — пока только `/welcome` (их интерфейсы впереди).
- `/api/*` при отсутствии входа отдаёт `401`, при нехватке роли — `403`; UI ведёт на `/login` или `/forbidden`.

## Примеры

Симуляция amoCRM (время задаётся явно — детерминированные правила 10:00):

```powershell
$sim = Invoke-RestMethod -Method Post -Uri http://localhost:5202/dev/amo/simulate `
  -ContentType "application/json" `
  -Body '{"scenario":"add-today-before-cutoff","now":"2026-10-27T06:30:00Z"}'

$orderId = $sim.order.id
```

Push операции курьера (повтор с тем же `clientOperationId` вернёт `isDuplicate = true`):

```powershell
$payload = @{ orderId = $orderId; toStatus = "accepted"; expectedVersion = 1 } | ConvertTo-Json -Compress
$opId = [guid]::NewGuid()

$body = @{
  deviceId = "demo-device"
  operations = @(@{
    clientOperationId = $opId
    type = "SetOrderStatus"
    payloadJson = $payload
    createdAt = "2026-10-27T12:00:00Z"
  })
} | ConvertTo-Json -Depth 6

Invoke-RestMethod -Method Post -Uri http://localhost:5202/api/sync/push `
  -ContentType "application/json" -Body $body
```

Pull изменений:

```powershell
Invoke-RestMethod "http://localhost:5202/api/sync/pull?since=0&limit=100"
```

Пустой `since`/`0` — с начала; `nextCursor` передавайте в следующий `since`. Курсор непрозрачный.

## Тесты

```powershell
dotnet test
```

Покрыто (30):
- intake (9): cutoff, перенос, прошлая дата, дубликат, отмена, точка на модерации, неизвестная точка, без ответственного;
- sync (7): идемпотентность push, конфликт версий, запрещённый переход, delta pull с tombstone, пагинация, изоляция тенантов, change feed из intake;
- точки (3): координаты и change feed, разблокировка заказов при модерации, дубликат внешнего ID;
- геокодер (4): прямой геокодинг, reverse 404, подсказки с дедупликацией, отсутствие ключа;
- маршруты (7): порядок/итоги/геометрия, авто-порядок, дубль в другом маршруте, блокировка после 10:00 и после подтверждения, пустой маршрут, дорожный роутер, дорожная матрица.

Auth/RBAC проверены HTTP-смоуком: анонимный доступ (302/401), вход, роли, неверный пароль, выход, `403` для курьера.

## Реализовано

- Cookie-аутентификация и RBAC: вход/выход, 6 ролей, защита UI и API, демо-учётки.
- `OrderIntakeService`: cutoff, перенос, дедупликация, отмена, блокировка точкой на модерации.
- `MockAmoIntake` + 9 фикстур + dev-симуляция.
- `SyncService`: `SetOrderStatus`, журнал операций, версии, change feed, tombstones.
- Blazor UI: `/orders`, `/orders/{id}`, `/routes`, `/points`, `/sync`; смена статуса через `SyncService`; dev-симуляция только в Development.
- 2ГИС: MapGL-карта, установка точек кликом, поиск адреса с подсказками, автоопределение адреса по координатам, модерация точек.
- Маршруты: 3 курьера, черновик на дату, остановки, авто-порядок по Distance Matrix + Or-opt, подтверждение, блокировка после 10:00, карта с цехом и дорожной линией (Directions API), демо-заказы.
- EF Core SQLite + `EnsureCreated`; Swagger UI.
- 30 зелёных тестов.

## Ограничения текущего этапа

- БД — SQLite; PostgreSQL/PostGIS ещё не подключены, миграций нет.
- Auth MVP: только пароль (без OTP), нет lockout/rate-limit и сброса пароля; выход через GET.
- Нет tenant-resolving — используется dev-тенант (claim `tenant_id` уже есть).
- Таблица `Users` добавляется в существующую dev-БД через `CREATE TABLE IF NOT EXISTS`; Swagger в Development анонимный.
- UI не покрыт e2e и не имеет пагинации; Blazor Server требует постоянного соединения (не офлайн-клиент).
- Карта проверяется в браузере: headless-WebGL не рендерит тайлы MapGL; автотестами покрыт только парсинг геокодера.
- Ключ 2ГИС — в user secrets (Development); для production нужен Vault/переменные окружения и ограничение по домену.
- Маршруты: нет TSP/VRP для нескольких курьеров/окон, ETA по остановкам, лимитов вместимости/точек; маршрут не синхронизируется курьеру.
- Дорожная геометрия — Directions API; авто-порядок — Distance Matrix + Or-opt. При ошибке/лимите демо-ключа 50 км — откат на прямую.
- Старые маршруты с прямой геометрией не пересчитываются автоматически.
- Подтверждение маршрута не меняет статусы заказов; трекинг/переназначение — впереди.
- Кеш геокодинга/маршрутов/матриц и async-матрица не подключены.
- Синхронизируется только смена статуса заказа; доставка/фото/тара — впереди.
- Нет ретенции change feed и device presence.
- `Order.Version` увеличивается вручную; EF concurrency token — следующий шаг.
- `SalesManagerUserId` всегда `null` (`AMO_USER_MAP` не реализован).
- Только мок amoCRM; реальный вебхук и подпись — впереди.
- `nextBusinessDay` не учитывает `WORKING_CALENDAR`.
- SQLite не поддерживает `ORDER BY` по `DateTimeOffset` — сортировка в памяти.

## Связанные документы

- Текущее состояние: `../30-plan/current-state.md`
- План мока: `../30-plan/mock-amo-plan.md`
- Протокол синка: `../20-decisions/ADR-0007-offline-sync.md`
- UI в хосте API: `../20-decisions/ADR-0008-blazor-ui-same-host.md`
- 2ГИС и точки: `../20-decisions/ADR-0009-2gis-maps.md`
- Аутентификация и роли: `../20-decisions/ADR-0010-authentication-rbac.md`
- Маршруты: `../20-decisions/ADR-0011-route-planner-mvp.md`
- Дорожная геометрия: `../20-decisions/ADR-0012-road-routing.md`
- Авто-порядок: `../20-decisions/ADR-0013-road-auto-order.md`
- База знаний: `../README.md`
