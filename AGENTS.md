# AGENTS.md — правила работы с базой знаний

Этот файл читают AI-ассистенты и разработчики **до** любых изменений.

## Обязательный порядок чтения в начале сессии

1. `README.md`
2. `30-plan/current-state.md`
3. `20-decisions/decision-log.md`
4. `40-questions/open-questions.md`
5. `10-spec/full-spec.md` — только нужные разделы, файл большой

## Обязанности по итогам сессии

- Обновить `30-plan/current-state.md`: что сделано, что дальше, блокеры.
- Добавить запись в `CHANGELOG.md` (дата + суть).
- Новое решение — создать ADR по шаблону и добавить строку в `decision-log.md`.
- Новый вопрос — добавить в `open-questions.md`, не в чат.
- Изменения в спецификации — править `10-spec/full-spec.md`, а не создавать «spec-v2-final» и т.п.

## Правила ведения

- **Не дублировать спецификацию.** Вспомогательные документы ссылаются на `full-spec.md`.
- **Не удалять историю решений.** ADR помечается `Superseded`, но остаётся.
- **Не менять статус ADR без обоснования.** Статусы: `Proposed`, `Accepted`, `Rejected`, `Superseded`, `Deprecated`.
- **Даты в ISO 8601.**
- **Язык документов — русский.** Код и идентификаторы — английские.
- **Один факт — одно место.** Если противоречие — приоритет у `full-spec.md` и `Accepted`-ADR.

## Соглашения по именованию

- Файлы: `kebab-case.md`.
- ADR: `ADR-NNNN-kratkoe-nazvanie.md`.
- Сущности в спеке: `UPPER_SNAKE_CASE` (`ORDER`, `ROUTE_POINT`).
- Enum-значения: `PascalCase` (`InTransit`, `PendingApproval`).

## Шаблон ADR

```markdown
# ADR-NNNN. Название

- Дата: YYYY-MM-DD
- Статус: Proposed | Accepted | Rejected | Superseded | Deprecated

## Контекст
Почему возник вопрос.

## Решение
Что решили.

## Последствия
Что становится проще, что сложнее, что нужно сделать.
```
