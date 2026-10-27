# ADR-0008. UI диспетчера: Blazor Server в одном хосте с API

- Дата: 2026-10-27
- Статус: Accepted

## Контекст

Нужна визуализация для проверки сценариев и работы диспетчера: заказы, карточка, смена статуса, лента синка. Отдельный SPA добавил бы второй проект, CORS, дублирование контрактов и время до первого экрана. ADR-0001 уже предполагает Blazor Server, но не фиксирует, где он живёт и как взаимодействует с API/синком.

## Решение

1. **Один хост.** Blazor Server подключён в `DeliverySystem.Api`: `AddRazorComponents().AddInteractiveServerComponents()`, `MapRazorComponents<App>().AddInteractiveServerRenderMode()`, `MapStaticAssets()`. REST остаётся под `/api`, Swagger — `/swagger`.
2. **Маршруты UI:** `/orders` (список + фильтры + статистика), `/orders/{id}` (карточка, позиции, история, переходы), `/sync` (change feed, журнал операций, dev-симуляция). Корень `/` остаётся JSON-навигацией.
3. **Нет scoped `DbContext` в circuit.** Компоненты получают `IServiceScopeFactory` и создают scope на операцию; чтение — `AsNoTracking`. Долгоживущий circuit не держит `DbContext`.
4. **Запись — только через `SyncService`.** Кнопка статуса формирует `SyncPushOperation(SetOrderStatus)` с `deviceId = "dispatcher-web"` и новым `ClientOperationId`, а не меняет статус напрямую. UI проходит тот же путь, что и курьер: идемпотентность, версия, change feed.
5. **Dev-инструменты только в Development.** Симуляция amoCRM в `/sync` скрыта вне Development; эндпоинты `/dev/amo/*` также только Development.
6. **Без JS-фреймворка.** Стили — один `wwwroot/app.css`. Интерактивность — Blazor Server (SignalR).

## Последствия

- Плюсы: один процесс и деплой, общие DI/контракты/домен, нет CORS, быстрый вертикальный срез, серверный доступ к БД без публичного API для каждого экрана.
- Минусы: нужен постоянный SignalR-соединение; при scale-out — sticky sessions или distributed cache; UI не работает офлайн. Blazor Server **не** клиент курьера — для офлайна остаются MAUI/PWA.
- Нет auth/RBAC: UI использует dev-тенант и показывает все заказы. До пилота обязателен вход и tenant-resolving.
- Список заказов и журнал операций сортируются в памяти (ограничение SQLite `DateTimeOffset`), пагинации нет.
- UI не покрыт e2e; проверен SSR-смоуком и ручным сценарием.

## Проверка

- `GET /orders` — 200, пустое состояние, затем заказ после симуляции.
- `GET /orders/{id}` — карточка, позиции, история, блок переходов.
- `GET /sync` — 200, change feed и журнал операций; после push видно `Applied` и устройство `smoke-dispatcher`.
- `GET /app.css` и `/_framework/blazor.web.js` — 200.
- Смена статуса через тот же `SyncService`: `Accepted`, версия 2, комментарий в истории.
- Тесты не затронуты: **16/16**.

## Что дальше

- Auth/OTP, RBAC, tenant-resolving; скрыть чужие заказы.
- Экраны маршрутов/курьеров и назначение; затем MAUI/PWA для офлайна.
- Пагинация/виртуализация списков, вынос чтения в query-слой при росте данных.
- Оценить необходимость отдельного SPA/API-клиентов (мобильное приложение, интеграции).
