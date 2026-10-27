# Реестр решений (decision log)

| ID | Дата | Решение | Статус |
|---|---|---|---|
| ADR-0001 | 2026-10-27 | Стек: .NET 8/9, Blazor Server, MAUI, PostgreSQL+PostGIS | Proposed |
| ADR-0002 | 2026-10-27 | Источник истины по статусу — `RoutePoint`, `Order.Status` вычисляется | Accepted |
| ADR-0003 | 2026-10-27 | Мок-интеграция amoCRM через порт `IOrderIntakeSource` | Accepted |
| ADR-0004 | 2026-10-27 | Один депо на тенант в MVP | Accepted |
| ADR-0005 | 2026-10-27 | Тарифы разделены на `ClientPrice` и `CourierPay` | Accepted |
| ADR-0006 | 2026-10-27 | Целевая платформа — .NET 10 | Accepted |
| ADR-0007 | 2026-10-27 | Офлайн-синк: change feed (`SYNC_CHANGE`) + идемпотентный push (`SYNC_OPERATION`) + `Order.Version` | Accepted |
| ADR-0008 | 2026-10-27 | UI диспетчера на Blazor Server в одном хосте с API; запись через `SyncService` | Accepted |
| ADR-0009 | 2026-10-27 | Провайдер карт — 2ГИС; экран `/points`, координаты в `Point` и разблокировка заказов при модерации | Accepted |
| ADR-0010 | 2026-10-27 | Cookie-аутентификация, 6 ролей, RBAC для UI и API, демо-пользователи в Development | Accepted |
| ADR-0011 | 2026-10-27 | MVP маршрутов: `Courier`/`Route`/`RoutePoint`, ручной и авто-порядок, блокировка после 10:00 | Accepted |
| ADR-0012 | 2026-10-27 | Дорожная геометрия маршрутов через Directions API 2ГИС, fallback на прямую | Accepted |
| ADR-0013 | 2026-10-27 | Авто-порядок остановок по Distance Matrix API + локальный Or-opt | Accepted |
| ADR-0014 | 2026-10-27 | Route-snapshot в change feed и веб-экран курьера `/my-routes` | Accepted |

## Как добавить решение

1. Создать `ADR-NNNN-kratkoe-nazvanie.md` по шаблону из `AGENTS.md`.
2. Добавить строку в эту таблицу.
3. Если решение заменяет старое — пометить старое `Superseded` и указать замену.
