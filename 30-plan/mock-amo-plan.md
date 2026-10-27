# План мок-интеграции amoCRM

> Реализует ADR-0003. Цель — приём заказов и все правила cutoff без реального amoCRM.

## 1. Порт

```csharp
public sealed record LeadEvent(
    Guid TenantId,
    string EventType,
    long AmoLeadId,
    long PipelineId,
    long StageId,
    string? StageName,
    long? ResponsibleAmoUserId,
    long? PointExternalId,
    DateOnly? RequestedDate,
    IReadOnlyList<LeadItem> Items,
    string RawJson,
    DateTimeOffset OccurredAt);

public interface IOrderIntakeSource
{
    Task<IntakeResult> HandleEventAsync(LeadEvent evt, CancellationToken ct = default);
}
```

`IntakeResult`: `Created | Updated | Deferred | Duplicate | Cancelled | ValidationError`, `OrderId`, `Reason`.

`OccurredAt` передаётся явно (а не берётся из `DateTime.UtcNow`) — это делает проверку правил 10:00 детерминированной.

## 2. Адаптеры

| Адаптер | Назначение |
|---|---|
| `MockAmoIntake` | Проигрывает фикстуры, пишет в `WEBHOOK_LOG` с `SignatureValid = true` |
| `AmoWebhookIntake` | Реальный, позже; подпись, `ExternalEventId` |

Оба используют **один** `OrderIntakeService` (домен): идемпотентность → cutoff → `Order` → history.

## 3. Фикстуры

Каталог `src/DeliverySystem.Infrastructure/Fixtures/amo/` (копируется в output через `CopyToOutputDirectory`). Сценарии:

| Файл | Сценарий | Ожидание |
|---|---|---|
| `add-today-before-cutoff.json` | Новая сделка «сегодня» до 10:00 | `Order` `New`, `IsDeferred=false` |
| `add-today-after-cutoff.json` | Новая сделка «сегодня» после 10:00 | `IsDeferred=true`, дата = nextBusinessDay |
| `add-tomorrow.json` | Сделка на завтра | `Order` `New` на завтра |
| `add-past-date.json` | Дата в прошлом | `ValidationError=InvalidDate`, не планируется |
| `duplicate-webhook.json` | Повтор того же события | `Duplicate`, второго `Order` нет |
| `cancel-lead.json` | Сделка снята с доставки | `Order` → `Cancelled` |
| `pending-point.json` | Точка на модерации | `IsBlockedByPointApproval=true` |
| `unknown-point.json` | Точки нет в системе | `ValidationError`, уведомление |
| `add-missing-responsible.json` | Нет ответственного | заказ создаётся, `SalesManagerUserId=null` |

## 4. Dev-эндпоинт

`POST /dev/amo/simulate` + `GET /dev/amo/scenarios` (только `Development`):

```json
{ "scenario": "add-today-after-cutoff", "leadId": 9001, "now": "2026-10-27T10:20:00+03:00" }
```

Позволяет задавать **искусственное «сейчас»** — критично для тестов 10:00.

## 5. Contract-тесты

Один `[Theory]`, два провайдера:

```csharp
[Theory]
[MemberData(nameof(IntakeProviders))]
public async Task Intake_AppliesCutoff(IOrderIntakeSource source) { ... }
```

Проверяем: статусы, `IsDeferred`, дедупликацию, `WEBHOOK_LOG`, `OrderStatusHistory`.

## 6. Definition of Done

- [x] Все 9 сценариев зелёные на `MockAmoIntake` (9/9).
- [x] `WEBHOOK_LOG` заполняется в мок-режиме (live-режим — впереди, см. §7).
- [x] Повторный вебхук не создаёт дубль (`ExternalEventId` + уникальный `AmoLeadId`).
- [x] Время управляемо в тестах (без `DateTime.Now`).
- [ ] Contract-тесты готовы принять `AmoWebhookIntake` без изменений — сейчас набор привязан к моку, общий провайдер ещё не выделен.
- [ ] Фикстуры — реальные снимки; текущие **синтетические**.

## 6.1. Что реализовано (факт)

| Артефакт | Путь |
|---|---|
| Порт и DTO | `src/DeliverySystem.Core/Intake/` |
| Сервис приёма | `src/DeliverySystem.Infrastructure/Intake/OrderIntakeService.cs` |
| Мок-адаптер | `src/DeliverySystem.Infrastructure/Intake/MockAmoIntake.cs` |
| Загрузка фикстур | `src/DeliverySystem.Infrastructure/Intake/AmoFixtureRepository.cs` |
| 9 тестов | `src/DeliverySystem.Tests/IntakeScenariosTests.cs` |
| Dev-эндпоинты | `src/DeliverySystem.Api/Program.cs` |

Отклонения от плана:

- Неизвестная точка: заказ не создаётся, пишется ошибка в `WEBHOOK_LOG`.
- `SalesManagerUserId` не заполняется (нет `AMO_USER_MAP`).
- `nextBusinessDay` учитывает только выходные, не `WORKING_CALENDAR`.
- Вместо PostgreSQL — SQLite.

## 7. Что мок НЕ покрывает (долг перед пилотом)

- Подпись вебхука и `AmoWebhookSecret`.
- Rate-limit, задержки, 5xx.
- PATCH обратно в CRM через `OUTBOX_EVENT`.
- `AMO_USER_MAP` и стадии конкретной воронки.
- Обновление фикстур при изменении API amoCRM.
