# ADR-0003. Мок-интеграция amoCRM

- Дата: 2026-10-27
- Статус: Accepted

## Контекст

Доступ к реальному amoCRM появится позже, но приём заказов — основа MVP. Мокать HTTP-границу плохо: домен и дедупликация не тестируются.

## Решение

- Ввести порт `IOrderIntakeSource` с методом `HandleEvent(LeadEvent, CancellationToken)`.
- `MockAmoIntake` проигрывает **реальные снимки payload'ов** из `tests/fixtures/amo/*.json` через тот же пайплайн: `WEBHOOK_LOG` → идемпотентность → BR-1 (cutoff) → `Order` → `OrderStatusHistory`.
- `AmoWebhookIntake` — реальный адаптер, подключается позже.
- Режим переключается флагом `IntegrationSettings.Mode = Mock | Live`.
- Один набор contract-тестов гоняется против обоих адаптеров.
- Dev-эндпоинт `POST /dev/simulate/amo-stage-change` для ручных сценариев.

## Последствия

- Плюс: домен, cutoff и дедупликация тестируются без amoCRM; не блокируемся на доступах.
- Минус: мок не проверяет подпись, rate-limit, PATCH обратно, реальные ID воронок.
- Нужно: 2–3 дня реального подключения перед пилотом + периодически обновлять фикстуры.
- См. `../30-plan/mock-amo-plan.md`.
