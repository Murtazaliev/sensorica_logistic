# ADR-0010. Аутентификация и RBAC: cookie-вход и роли

- Дата: 2026-10-27
- Статус: Accepted

## Контекст

До этого этапа API и UI были открыты, использовался dev-тенант. В спецификации (Часть 4) шесть ролей: `Admin`, `Dispatcher`, `Courier`, `PointManager`, `SalesManager`, `Head`. Для пилота нужны вход, защита страниц/API и разграничение доступа. OTP и управление пользователями — отдельные этапы; для MVP достаточно пароля и cookie-сессии.

## Решение

1. **Cookie-аутентификация ASP.NET Core.** Схема `Cookies`, cookie `ds.auth` (`HttpOnly`, `SameSite=Lax`, `SecurePolicy=SameAsRequest`), срок 8 часов со скольжением. Пароли хранятся через `PasswordHasher<AppUser>` (PBKDF2), plaintext не хранится.
2. **Пользователь.** Сущность `AppUser` — подмножество `USER` из спеки: `TenantId`, уникальный `Email`, `FullName`, `PasswordHash`, `Role`, `IsActive`, `LastLoginAt`, `PreferredLanguage`. Телефон и OTP-поля — следующий этап.
3. **Вход.** `GET /login` — статическая HTML-страница с antiforgery-токеном и демо-доступами (только Development); `POST /auth/login` — проверка пароля, `SignInAsync`, редирект на `returnUrl` (только локальный) или на домашнюю страницу роли. `GET /auth/logout` — выход.
4. **Роли и политики.**
   - `Staff` = `Dispatcher`, `Admin`, `Head`;
   - `AdminOnly` = `Admin`.
5. **Защита UI.** Страницы `/orders`, `/orders/{id}`, `/points` — Staff; `/sync` — `Admin`/`Dispatcher`; `/welcome` — любой авторизованный; `/forbidden` — страница 403. Компоненты используют `AuthorizeRouteView`, `[Authorize]` и `AuthorizeView`; навигация скрывает недоступные разделы.
6. **Защита API.** `/api/orders*` — Staff; `POST /api/points` — Staff; `/api/webhook-logs` — Admin; `/api/sync/*`, `GET /api/points`, `/api/geocode*`, `/api/suggest` — любой авторизованный; `/dev/amo/*` — Staff (только Development). Для `/api/*` вместо редиректов возвращаются `401`/`403`.
7. **Демо-пользователи.** В Development сидятся восемь учёток с паролем `demo123`:
   - `admin@demo.local`, `dispatcher@demo.local`, `head@demo.local`,
   - `courier@demo.local`, `courier2@demo.local`, `courier3@demo.local`,
   - `point@demo.local`, `sales@demo.local`.
   Пароль демонстрационный; в production пользователи и секреты создаются отдельно.
8. **Dev-схема.** Для существующей SQLite-БД таблица `Users` создаётся `CREATE TABLE IF NOT EXISTS` в `DevSeed`; полноценные EF-миграции — следующий этап.

## Последствия

- Плюсы: страницы и API закрыты, роли проверяются на endpoint и в UI, есть понятные 401/403 и страница `/forbidden`; существующая dev-БД обновляется без удаления.
- Минусы: только пароль, без OTP/блокировки/лимита попыток; выход через GET (CSRF-выход возможен, но не критичен); Swagger в Development остаётся анонимным; таблица `Users` создаётся вручную до миграций; курьер/менеджер точки/менеджер продаж пока видят только `/welcome`.
- `tenant_id` уже в claims, но tenant-resolving ещё не реализован — используется dev-тенант.

## Проверка

- Анонимный `/orders` → 302 на `/login`; `/api/orders` → 401.
- Диспетчер: вход, `/orders`, `/points`, `/sync` → 200; создание точки через API → 200.
- Курьер: вход → `/welcome`; `/orders` → 302 на `/forbidden`; `/api/orders` и `POST /api/points` → 403; `GET /api/points` → 200.
- Руководитель: `/orders` 200, `/sync` 403.
- Администратор: `/api/webhook-logs` 200.
- Неверный пароль → `/login?error=1`; выход → `/orders` снова требует вход.
- Существующая dev-БД: таблица `Users` создана, вход работает.
- Тесты: **23/23** на момент auth; текущее состояние — **30/30** (RBAC проверен HTTP-смоуком).

## Что дальше

- OTP для курьера и менеджера точки (`OTP_CODE`), восстановление пароля.
- Управление пользователями и ролями (`/admin/users`), блокировка, сброс пароля.
- Lockout/rate-limit входа, аудит `LastLoginAt`/событий.
- POST-выход с antiforgery, защита Swagger, продакшн-хранение секретов и HTTPS.
