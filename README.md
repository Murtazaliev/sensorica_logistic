# База знаний: Система доставки полуфабрикатов

> Единая точка входа для людей и AI-ассистентов. Главная цель — чтобы контекст не терялся между сессиями и перезагрузками.

## Как пользоваться

- Начинай с этого файла и `30-plan/current-state.md`.
- Каноническая спецификация — `10-spec/full-spec.md`. **Не дублируй её**, ссылайся.
- Любое архитектурное/продуктовое решение — отдельный ADR в `20-decisions/`.
- Любое изменение — запись в `CHANGELOG.md` + обновление `current-state.md`.

## Структура

```text
delivery-system-kb/
├── README.md                      # этот файл, навигация
├── AGENTS.md                      # правила для AI-ассистентов и контрибьюторов
├── CHANGELOG.md                   # история изменений базы знаний
├── 00-context/
│   ├── project-brief.md           # что строим, цель, scope in/out, метрики
│   ├── glossary.md                # термины RU ↔ EN ↔ код
│   └── stack.md                   # предлагаемый стек и его статус
├── 10-spec/
│   └── full-spec.md               # КАНОНИЧЕСКАЯ спецификация v2.0
├── 20-decisions/
│   ├── decision-log.md            # реестр всех решений
│   ├── ADR-0001-stack.md
│   ├── ADR-0002-status-source-of-truth.md
│   ├── ADR-0003-mock-intake.md
│   ├── ADR-0004-single-depot.md
│   ├── ADR-0005-tariffs-split.md
│   ├── ADR-0006-dotnet10.md
│   ├── ADR-0007-offline-sync.md
│   ├── ADR-0008-blazor-ui-same-host.md
│   ├── ADR-0009-2gis-maps.md
│   ├── ADR-0010-authentication-rbac.md
│   ├── ADR-0011-route-planner-mvp.md
│   ├── ADR-0012-road-routing.md
│   └── ADR-0013-road-auto-order.md
├── 30-plan/
│   ├── roadmap.md                 # этапы, сроки, риски, MVP-скоуп
│   ├── current-state.md           # где мы сейчас и что дальше (обновлять!)
│   └── mock-amo-plan.md           # план и статус мока amoCRM
├── 40-questions/
│   └── open-questions.md          # нерешённые вопросы
└── src/                           # исходный код (.NET 10)
    ├── README.md                  # как запускать и что реализовано
    ├── DeliverySystem.slnx
    ├── DeliverySystem.Core/
    ├── DeliverySystem.Infrastructure/
    ├── DeliverySystem.Api/
    └── DeliverySystem.Tests/
```

## Правила

- **Single source of truth:** факты о системе — в `full-spec.md`; решения — в ADR; состояние — в `current-state.md`.
- **Ссылки, а не копии.** Если факт нужен в нескольких местах — ссылайся на раздел.
- **Даты только ISO 8601** (`2026-10-27`).
- **Каждое решение имеет ADR** с датой, статусом и последствиями.
- **Открытый вопрос** — это не «TODO в голове», а запись в `40-questions/open-questions.md`.

## Быстрый старт для новой сессии

1. Прочитать `README.md` (этот файл).
2. Прочитать `30-plan/current-state.md` — что сделано, что дальше.
3. Просмотреть `20-decisions/decision-log.md` — действующие решения.
4. Просмотреть `40-questions/open-questions.md` — что не решено.
5. При необходимости — `10-spec/full-spec.md` (полная спецификация).
6. Продолжить с раздела «Следующие шаги» в `current-state.md`.

## Текущее состояние (кратко)

- Фаза: **разработка начата**; UI диспетчера, 2ГИС, auth/RBAC и MVP маршрутов готовы.
- Спека: **v2.0** (после ревью на пробелы).
- Код: `src/` на .NET 10, **30/30 тестов зелёные**, Swagger UI, экраны `/orders`, `/routes`, `/points`, `/sync`, вход `/login`.
- Следующая цель: TSP/VRP и ETA по остановкам, затем OTP и управление пользователями.
- Подробнее: `30-plan/current-state.md`.
