# ADR-0016. Идемпотентный API действий курьера

- Дата: 2026-10-27
- Статус: Accepted

## Контекст

Курьер выполняет действия через Blazor-страницу, которая вызывает `RouteService` напрямую. Для офлайн-клиента нужны HTTP-операции с `clientOperationId`, чтобы повторная отправка после обрыва связи не дублировала приём/доставку/проблему.

## Решение

1. **Новые операции.** `SyncOperationType`: `AcceptRoute`, `StartRoute`, `ArriveRoutePoint`, `DeliverRoutePoint`, `ProblemRoutePoint`. Payload'ы: `RouteOperationPayload`, `RoutePointOperationPayload`, `RoutePointProblemPayload`.
2. **`PushCourierAsync`.** `ISyncService` получил метод с `courierId`. Существующий `PushAsync` без курьера для route-операций возвращает `courier_required`.
3. **Идемпотентность.** Используется существующий журнал `SYNC_OPERATION` (UNIQUE `TenantId + ClientOperationId`). Вся route-операция и запись журнала — в одной транзакции; повтор возвращает сохранённый `Applied` с `isDuplicate=true`, состояние не меняется.
4. **Применение.** `SyncService` делегирует операцию в `RouteService` (accept/start/arrive/deliver/problem), поэтому сохраняются все проверки, статусы заказов, история, версии и `SYNC_CHANGE`.
5. **API.** `POST /api/courier/sync/push` (роль `Courier`) по cookie определяет `AppUser.Id` → `Courier.UserId` и вызывает `PushCourierAsync`. Диспетчер/админ получают `403`. Результат — `SyncPushResult`; успешный `ResultJson` содержит route details.
6. **Страница курьера** пока продолжает вызывать `RouteService` напрямую (проще для интерактивного UI); мобильный клиент будет использовать API.

## Последствия

- Плюсы: безопасные ретраи действий курьера; журнал операций; API готов для MAUI/PWA; route/order изменения идемпотентны.
- Минусы: ошибки route-операций пока в виде `route_error: <текст>`, без полного набора машинных кодов; API использует cookie-сессию, для мобильного нужен токен/OTP; нет rate-limit и подписи устройства; UI и API — два пути вызова (риск расхождения, покрыт общими сервисами).
- Транзакция включает только БД; внешние вызовы (Directions/Matrix) в route-операциях не участвуют.

## Проверка

- Тесты: accept идемпотентен (второй вызов `isDuplicate=true`); полный цикл accept → start → arrive → deliver → problem через sync; **37/37**.
- HTTP-смоук: `AcceptRoute` → `Applied`, повтор → `Applied + isDuplicate=true`; диспетчер на `/api/courier/sync/push` → `403`.

## Что дальше

- Офлайн-клиент MAUI/PWA: локальная очередь операций, повтор с тем же `clientOperationId`.
- Машинные коды ошибок route-операций, версия маршрута/конфликты.
- OTP/токены для мобильного API, rate-limit и аудит.
