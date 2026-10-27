# Current state

- Обновлено: **2026-10-27**
- Фаза: **разработка начата**; UI диспетчера, 2ГИС, auth/RBAC и **MVP маршрутов** готовы
- Ближайшая цель: **TSP/VRP и ETA по остановкам**, затем OTP и управление пользователями
- Код: `src/DeliverySystem.slnx` (.NET 10), **30/30 тестов зелёные**

## Сделано

- [x] Спецификация v2.0 после ревью на пробелы (`10-spec/full-spec.md`).
- [x] Разрешены 7 открытых вопросов (дефолты в разделе 7.2 спеки).
- [x] Создана база знаний.
- [x] Принято решение мокать amoCRM на уровне порта (ADR-0003).
- [x] Целевая платформа — .NET 10 (ADR-0006).
- [x] Решение на 4 проекта: Core / Infrastructure / Api / Tests.
- [x] Домен: `Tenant`, `Point`, `Order`, `OrderItem`, `OrderStatusHistory`, `WebhookLog`, enum'ы.
- [x] `OrderIntakeService`: cutoff, перенос на следующий рабочий день, `InvalidDate`, дедупликация, отмена, блокировка точкой на модерации.
- [x] `MockAmoIntake` + 9 фикстур + dev-симуляция стадий.
- [x] **Спайк офлайн-синка** (ADR-0007): `SYNC_CHANGE` (delta pull + tombstones), `SYNC_OPERATION` (идемпотентный push), `Order.Version` (конфликты), `SetOrderStatus`.
- [x] Change feed пополняется из intake-а amoCRM (создание/обновление/отмена).
- [x] Swagger UI (`/swagger`, только Development).
- [x] Валидация dev-симуляции: `400 invalid_now` / `unknown_scenario`, сценарий только из списка фикстур.
- [x] **UI-срез диспетчера** (ADR-0008): Blazor Server в хосте API — `/orders`, `/orders/{id}`, `/points`, `/sync`; смена статуса через `SyncService` (`deviceId = dispatcher-web`); dev-симуляция только в Development; русская локализация статусов, устройств и системных комментариев.
- [x] **Интеграция карт 2ГИС и точек доставки** (ADR-0009): `Point.Latitude/Longitude`, `PointService`, экран `/points` с **MapGL JS API** (пины статусов, установка кликом, поиск адреса с подсказками, автоопределение адреса по клику), модерация с разблокировкой заказов и записью в `SYNC_CHANGE`, API `/api/points`.
- [x] **2ГИС Geocoder/Suggest** (ADR-0009): `TwoGisGeocoder`, `GET /api/geocode`, `/api/geocode/reverse`, `/api/suggest`; ключ в .NET user secrets (`TwoGis:ApiKey`), не в репозитории.
- [x] Стили UI (`wwwroot/app.css`), JS-мост MapGL (`wwwroot/map.js`, `wwwroot/icons/`), SSR-смоук всех страниц.
- [x] **Аутентификация и RBAC MVP** (ADR-0010): cookie-сессия, пароли через `PasswordHasher`, 6 ролей, 8 демо-учёток (`demo123`), `/login`, `/auth/login`, `/auth/logout`, страница `/forbidden`.
- [x] **MVP маршрутов курьеров** (ADR-0011): `Courier`/`Route`/`RoutePoint`, 3 курьера (`courier@`, `courier2@`, `courier3@demo.local`), черновик на курьера/дату, ручной и авто-порядок по прямой, подтверждение и блокировка после 10:00.
- [x] **Дорожная геометрия маршрутов** (ADR-0012): Directions API 2ГИС, дистанция/время, цех как старт, fallback на прямую; MapGL рисует дорожную линию.
- [x] **Авто-порядок по дорогам** (ADR-0013): Distance Matrix API + Or-opt, fallback на прямую; в смоуке дистанция 11,15 → 6,85 км.
- [x] Страница `/routes` (Staff): список маршрутов на дату, свободные заказы, остановки, карта MapGL с цехом/номерами/линией, демо-заказы в Development; API `/api/couriers`, `/api/routes`, `/dev/orders/demo`.
- [x] Цех в конфиге `Depot` (`Name`, `Address`, `Latitude`, `Longitude` в `appsettings.json`); маркер и старт линии на карте.
- [x] Защита UI: `/orders`, `/routes`, `/points` — Staff; `/sync` — Admin/Dispatcher; `/welcome` — все авторизованные; скрытие разделов в меню.
- [x] Защита API: Staff/Admin/любой авторизованный по разделам; `401/403` для `/api/*`; `/dev/amo/*` и `/dev/orders/*` только Staff.
- [x] **30/30 тестов зелёные** (маршруты, дорожный роутер, дорожная матрица); auth и маршруты проверены HTTP-смоуком.
- [x] EF Core 10 + SQLite, `EnsureCreated` + автодобавление таблиц/колонок в существующую dev-БД.
- [x] API и UI проверены смоуком; ключ 2ГИС и тайлы отвечают 200.

## Не завершено

- [ ] Auth MVP без OTP, блокировки/лимита попыток, сброса пароля и управления пользователями; выход через GET.
- [ ] Tenant-resolving не реализован: используется dev-тенант, хотя `tenant_id` уже в claims.
- [ ] UI не покрыт e2e; нет пагинации заказов и журнала операций (маленькие выборки dev-этапа).
- [ ] Маршруты: нет TSP/VRP для нескольких курьеров и временных окон; ETA по остановкам, лимиты вместимости/точек.
- [ ] Маршрут не уходит курьеру в change feed; нет экрана маршрута в MAUI/PWA.
- [ ] Старые подтверждённые маршруты сохраняют прямую геометрию; новые/изменённые строятся по дорогам.
- [ ] Подтверждение маршрута не меняет статусы заказов; трекинг/переназначение — впереди.
- [ ] TSP/VRP и async Distance Matrix не подключены; геокодинг/маршрутизация/матрица без кеша и лимитов; демо-маршрут ограничен 50 км.
- [ ] Визуальный рендер карты проверяется в браузере: headless-WebGL тайлы MapGL не рисует.
- [ ] Синхронизируется только `SetOrderStatus`. Нет `Delivery`, `DeliveryPhoto`, `TareMovement`, `Problem`.
- [ ] Нет ретенции/компакции `SYNC_CHANGE` и снапшотов для холодного старта.
- [ ] Нет `USER_DEVICE.LastSeenAt` и алерта «офлайн > N».
- [ ] `Version++` вручную — заменить на EF concurrency token/interceptor.
- [ ] Contract-тесты привязаны к моку; общий провайдер для реального адаптера не выделен.
- [ ] Фикстуры amoCRM **синтетические**.
- [ ] EF-миграции отсутствуют; dev-таблицы/колонки добавляются вручную, для произвольных изменений схемы — пересоздание `delivery.db`.
- [ ] `SalesManagerUserId` всегда `null`; `nextBusinessDay` не читает `WORKING_CALENDAR`.

## Следующие шаги (по порядку)

1. Добить фундамент: OTP для курьера/точки, управление пользователями, lockout/rate-limit, аудит, `OUTBOX_EVENT`, `SCHEDULED_JOB` (этап 1 roadmap).
2. Довести маршруты: Directions/TSP и дорожная геометрия, лимиты вместимости/окон, статусы заказов, синк маршрута курьеру (этап 2).
3. EF concurrency token вместо ручного `Version++`; миграции и PostgreSQL.
4. Ретенция change feed, device presence, полный набор sync-операций (доставка/фото/тара).
5. Реальные payload'ы amoCRM и общий contract-набор для мок/реального адаптера.
6. Клиент курьера с офлайном (MAUI/PWA) — после стабилизации протокола синка.
7. TSP/VRP для нескольких курьеров и окон, async-матрица/кеш, ограничение ключа по домену.

## Блокеры / зависимости

- Реальный доступ к amoCRM — перед пилотом (этап 11), не сейчас.
- Не утверждён хостинг и требования 152-ФЗ (`00-context/stack.md`).

## Известные отклонения от спеки

- Неизвестная точка: заказ **не создаётся**, пишется `WEBHOOK_LOG.ProcessingError` и возвращается `ValidationError`.
- `Order.Point` помечен `[JsonIgnore]` — навигация не сериализуется.
- Сортировка по `DateTimeOffset` выполняется в памяти (ограничение SQLite), в том числе для журнала операций в UI.
- UI требует постоянного SignalR-соединения (Blazor Server); для офлайна курьера он не используется.
- Демо-пароль всех учёток — `demo123` (только Development); Swagger в Development анонимный.
- Таблицы `Users`, `Couriers`, `Routes`, `RoutePoints` для существующей dev-SQLite создаются через `CREATE TABLE IF NOT EXISTS`; миграций пока нет.
- Маршрут: геометрия/дистанция/время из Directions API, авто-порядок — Distance Matrix + локальный поиск; при ошибке/лимите 50 км — прямая линия и оценка. Лимиты курьера не блокируют маршрут.
- Назначение в маршрут не меняет статус заказа (остаётся `New`/`Scheduled`); маршрут пока не синхронизируется курьеру.
- Курсор sync — `SYNC_CHANGE.Id` (глобальный autoincrement) с фильтром по тенанту, а не per-tenant sequence.
- `SYNC_OPERATION` хранит операции вечно (ретенция — позже).

## Карта кода

| Что | Где |
|---|---|
| Сущности и enum'ы | `src/DeliverySystem.Core/Domain/` |
| Контракты приёма | `src/DeliverySystem.Core/Intake/` |
| Контракты синка | `src/DeliverySystem.Core/Sync/SyncContracts.cs` |
| `OrderIntakeService` | `src/DeliverySystem.Infrastructure/Intake/OrderIntakeService.cs` |
| `MockAmoIntake` | `src/DeliverySystem.Infrastructure/Intake/MockAmoIntake.cs` |
| `SyncService` | `src/DeliverySystem.Infrastructure/Sync/SyncService.cs` |
| `PointService` | `src/DeliverySystem.Infrastructure/Points/PointService.cs` |
| `RouteService` | `src/DeliverySystem.Infrastructure/Routes/RouteService.cs` |
| Демо-заказы | `src/DeliverySystem.Infrastructure/Demo/DemoOrderService.cs` |
| Геокодер 2ГИС | `src/DeliverySystem.Infrastructure/Maps/TwoGisGeocoder.cs` |
| Дорожный роутер | `src/DeliverySystem.Infrastructure/Maps/TwoGisRouter.cs` |
| Дорожная матрица | `src/DeliverySystem.Infrastructure/Maps/TwoGisDistanceMatrix.cs` |
| Пользователь / роли | `src/DeliverySystem.Core/Domain/AppUser.cs`, `Enums.cs` |
| Вход и RBAC | `src/DeliverySystem.Api/Program.cs` (`/login`, `/auth/login`, политики) |
| Демо-учётки | `src/DeliverySystem.Api/DevSeed.cs` |
| Фикстуры | `src/DeliverySystem.Infrastructure/Fixtures/amo/` |
| DbContext | `src/DeliverySystem.Infrastructure/Persistence/AppDbContext.cs` |
| API + Swagger | `src/DeliverySystem.Api/Program.cs` |
| UI (Blazor) | `src/DeliverySystem.Api/Components/` (`Pages/Orders`, `Pages/OrderDetail`, `Pages/RoutePlanner`, `Pages/Points`, `Pages/SyncFeed`, `Pages/Welcome`, `Pages/Forbidden`) |
| Карта 2ГИС и стили | `src/DeliverySystem.Api/wwwroot/map.js`, `route-map.js`, `wwwroot/icons/`, `wwwroot/app.css` |
| Конфиг цеха | `src/DeliverySystem.Api/appsettings.json` (`Depot`) |
| Тесты | `src/DeliverySystem.Tests/` (`IntakeScenariosTests`, `SyncScenariosTests`, `PointScenariosTests`, `RouteScenariosTests`, `TwoGisGeocoderTests`, `TestDatabase`) |

## Открытые вопросы

См. `40-questions/open-questions.md`.
