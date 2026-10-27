# Система доставки полуфабрикатов — консолидированная спецификация

> Версия 2.0 (после ревью на пробелы). Включает: сущности, статусы, роли/экраны, уведомления, офлайн-синхронизацию и сквозные сценарии.
> Все спорные места из ревью разрешены; принятые допущения вынесены в Часть 7.

---

## Часть 0. Бизнес-правила и временные окна

### 0.1. Параметры тенанта

| Параметр | По умолчанию | Смысл |
|---|---|---|
| `DeliveryStartTime` | 10:00 | С этого времени курьер может забрать заказы в цехе |
| `CutoffTimeForTodayOrders` | 10:00 | После этого времени заказы «на сегодня» не принимаются |
| `PlanningLockTime` | 10:00 | После этого времени маршруты на сегодня блокируются |
| `CourierLateStartGraceMinutes` | 15 | Через сколько после старта считать, что курьер не выехал |
| `MaxPointsPerCourier` | 30 | Лимит точек на курьера в день |
| `MaxWeightPerCourier` | 500 кг | Лимит веса на курьера в день |
| `GeofenceRadiusMeters` | 200 | Радиус геофенса вокруг депо для «Забрал в цехе» |
| `DefaultDeliveryWindowFrom/To` | 09:00 / 18:00 | Окно доставки по умолчанию для новых точек |
| `RequirePointApprovalForManager` | false | Модерация точек, созданных менеджером продаж |
| `AutoConfirmDeliveryAfterMinutes` | 60 | Авто-подтверждение приёмки, если точка не подтвердила |
| `ReopenDeliveryWindowDays` | 3 | Сколько дней после `ЗАВЕРШЁН` можно открыть спор |

### 0.2. Инварианты времени (проверяются при сохранении настроек)

1. `DeliveryStartTime <= PlanningLockTime`
2. `CutoffTimeForTodayOrders <= PlanningLockTime`
3. Все `DateTime` хранятся в **UTC**; бизнес-окна интерпретируются в `Tenant.TimeZone`.
4. Сравнение порогов инклюзивное: `now >= CutoffTime` → заказ «на сегодня» переносится.

### 0.3. Правила (BR)

- **BR-1. Приём заказа из amoCRM.**
  - `RequestedDate > today` → принимается как есть, `Status = New`.
  - `RequestedDate = today` и `now < CutoffTime` → принимается на сегодня.
  - `RequestedDate = today` и `now >= CutoffTime` → `OriginalRequestedDate = today`, `RequestedDate = nextBusinessDay(from = today)`, `IsDeferred = true`, `DeferredReason = AfterCutoff`.
  - `RequestedDate < today` → `ValidationError = InvalidDate`, `Status = New`, планирование заблокировано до исправления.
- **BR-2. Блокировка планирования.** В `PlanningLockTime` все `Route` на сегодня получают `IsLocked = true`, `LockedAt`. Добавление/удаление точек и смена курьера запрещены. Исключение — действия через `Problem` (переназначение на будущую дату).
- **BR-3. Старт курьера.** «Забрал в цехе» доступно только с `DeliveryStartTime` и при нахождении внутри геофенса `GeofenceRadiusMeters` от депо.
- **BR-4. После `PlanningLockTime`** экран планирования диспетчера открывается на «Завтра»; сегодняшние маршруты — read-only.
- **BR-5. Новые заказы после `CutoffTime`** автоматически уходят на `nextBusinessDay`, даже если менеджер указал «сегодня».
- **BR-6. Рабочий календарь.** `nextBusinessDay` учитывает `WORKING_CALENDAR` (выходные, праздники, особые дни). Если календарь не задан — сб–вс нерабочие.
- **BR-7. Лимиты курьера.** При планировании проверяются `MaxPointsPerCourier`, `MaxWeightPerCourier` и вместимость ТС; превышение — предупреждение с подтверждением (soft-limit) или запрет (если `Tenant.StrictCapacity = true`).
- **BR-8. Создание точек.** Админ и руководитель создают сразу `Active`. Менеджер продаж — `Active` или `PendingApproval` в зависимости от `RequirePointApprovalForManager`. Если ни у кого нет роли `Head`, модерацию выполняет админ (фолбэк).
- **BR-9. Дубликаты точек.** Проверка по нормализованному адресу и координатам (радиус 50 м). При совпадении — предупреждение с выбором «Открыть существующую» / «Всё равно создать».
- **BR-10. Заказ на точку в `PendingApproval`** принимается, но `IsBlockedByPointApproval = true`; планирование запрещено до одобрения.
- **BR-11. Отмена в пути.** Менеджер/диспетчер может отменить заказ в статусе `InTransit`; требуется подтверждение курьера (push «Заказ отменён»), `RoutePoint.Status = Cancelled`.
- **BR-12. Переоткрытие/спор.** Из `Completed` можно перейти в `Problem` в течение `ReopenDeliveryWindowDays` (только Head/Admin), с фиксацией причины.
- **BR-13. Идемпотентность.** Все клиентские операции несут `ClientOperationId`; повторная отправка не создаёт дублей.
- **BR-14. Единый источник истины.** Оперативный статус хранится в `RoutePoint`; `Order.Status` обновляется доменным сервисом в той же транзакции и дублируется в `OrderStatusHistory`.

---

## Часть 1. ER-модель

> Общие поля всех изменяемых сущностей (далее не повторяются): `Id (Guid PK)`, `CreatedAt`, `UpdatedAt`, `RowVersion` (optimistic concurrency), `IsDeleted`, `DeletedAt`. Все `TenantId`-сущности scoped по тенанту; доступ проверяется на уровне запросов и политик.

### 1.1. Тенант и пользователи

```text
TENANT
├── Id (PK, Guid)
├── Name
├── Address
├── Lat, Lng                      # координаты депо
├── TimeZone
├── DeliveryStartTime             # time, default 10:00
├── CutoffTimeForTodayOrders      # time, default 10:00
├── PlanningLockTime              # time, default 10:00
├── CourierLateStartGraceMinutes  # int, default 15
├── MaxPointsPerCourier           # int, default 30
├── MaxWeightPerCourier           # decimal, default 500
├── StrictCapacity                # bool, default false
├── GeofenceRadiusMeters          # int, default 200
├── DefaultDeliveryWindowFrom     # time, default 09:00
├── DefaultDeliveryWindowTo       # time, default 18:00
├── RequirePointApprovalForManager# bool, default false
├── AutoConfirmDeliveryAfterMinutes # int, default 60
├── ReopenDeliveryWindowDays      # int, default 3
└── IsActive
```
```text
USER
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── Phone                # уникален в тенанте
├── Email
├── FullName
├── PasswordHash         # null для входа только по OTP
├── Role                 # Admin | Dispatcher | Courier | PointManager | SalesManager | Head
├── IsActive
├── LastLoginAt
└── PreferredLanguage
```
```text
USER_DEVICE
├── Id (PK)
├── UserId (FK → User)
├── PushToken
├── Platform             # iOS | Android | Web
├── LastSeenAt
└── IsActive
```
```text
OTP_CODE
├── Id (PK)
├── UserId (FK → User)
├── CodeHash
├── ExpiresAt
├── Attempts
├── CooldownUntil
└── ConsumedAt (nullable)
```
```text
USER_SESSION
├── Id (PK)
├── UserId (FK → User)
├── RefreshTokenHash
├── DeviceId (FK → UserDevice, nullable)
├── ExpiresAt
├── RevokedAt (nullable)
└── IpAddress
```
```text
COURIER
├── Id (PK)
├── UserId (FK → User, 1:1)
├── TenantId (FK → Tenant)
├── Type                 # Staff | Contractor | SelfEmployed
├── PassportSeries, PassportNumber
├── INN
├── VehicleType          # Car | Motorcycle | Bicycle | OnFoot
├── VehiclePlate
├── CapacityWeight       # кг
├── CapacityVolume       # м³
├── MaxPointsPerDay      # override тенантского лимита
├── Rating               # вычисляемый кэш (см. DeliveryFeedback)
├── IsAvailable
└── BlockedReason (nullable)
```
```text
COURIER_AVAILABILITY
├── Id (PK)
├── CourierId (FK → Courier)
├── DateFrom (DateOnly)
├── DateTo (DateOnly)
├── Reason               # Vacation | Sick | Personal | Other
└── Comment
```
```text
DELIVERY_FEEDBACK          # источник для Courier.Rating
├── Id (PK)
├── DeliveryId (FK → Delivery)
├── RatedByUserId (FK → User)
├── Score (int, 1..5)
├── Comment
└── CreatedAt
```
```text
AMO_USER_MAP               # сопоставление ответственных amoCRM ↔ наши пользователи
├── Id (PK)
├── TenantId (FK → Tenant)
├── AmoUserId (long)
├── UserId (FK → User)
└── IsActive
```

### 1.2. Точки

```text
POINT
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── Name
├── Address
├── NormalizedAddress
├── Location             # GEOGRAPHY(Point, 4326), GIST-индекс
├── Phone
├── ContactPerson
├── DeliveryWindowFrom (time)
├── DeliveryWindowTo (time)
├── QrCode (nullable)
├── Comment
├── IsActive
├── CreatedByUserId (FK → User)
├── Source               # Admin | Head | SalesManager | Import | AmoCRM
├── ApprovalStatus       # Active | PendingApproval | Rejected
├── ApprovedByUserId (FK → User, nullable)
├── ApprovedAt (nullable)
├── RejectionReason (nullable)
├── UserId (FK → User, nullable)   # учётка менеджера точки (PWA)
└── ValidationError (nullable)     # например GeocodeFailed
```
```text
POINT_CONTACT
├── Id (PK)
├── PointId (FK → Point)
├── FullName
├── Phone
├── Role                 # Director | Storekeeper | Other
└── IsPrimary
```
```text
POINT_SCHEDULE
├── Id (PK)
├── PointId (FK → Point)
├── DayOfWeek (0..6)
├── From (time)
├── To (time)
└── IsWorking
```
```text
POINT_DAY_OFF
├── Id (PK)
├── PointId (FK → Point)
├── Date (DateOnly)
└── Reason
```
```text
POINT_CREATION_LOG
├── Id (PK)
├── TenantId (FK → Tenant)
├── PointId (FK → Point)
├── UserId (FK → User)
├── Action               # Created | Updated | Approved | Rejected | Deactivated | Resubmitted
├── OldValueJson (jsonb, nullable)
├── NewValueJson (jsonb, nullable)
├── Comment
└── CreatedAt
```

### 1.3. Заказы

```text
ORDER
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── OrderNumber                  # ORD-YYYY-NNNNNN, уникален в тенанте
├── AmoLeadId (nullable, UNIQUE(TenantId, AmoLeadId))
├── PointId (FK → Point)
├── SalesManagerUserId (FK → User, nullable)
├── RequestedDate (DateOnly)
├── OriginalRequestedDate (DateOnly)
├── IsDeferred (bool)
├── DeferredReason               # AfterCutoff | InvalidDate | Manual | null
├── ValidationError              # InvalidDate | MissingItems | null
├── IsBlockedByPointApproval (bool)
├── ReceivedAt (DateTime, UTC)
├── Status                       # см. Часть 3
├── TotalWeight (decimal 10,3)
├── TotalVolume (decimal 12,4)
├── TotalAmount (numeric 18,2)
├── Currency (char 3)
├── Comment
├── CancelledReason (nullable)
└── CancelledByUserId (FK → User, nullable)
```
```text
ORDER_ITEM
├── Id (PK)
├── OrderId (FK → Order)
├── ProductName
├── Quantity (decimal)
├── Unit                   # kg | pcs | pack
├── Weight (decimal)
└── Comment
```
```text
ORDER_STATUS_HISTORY
├── Id (PK)
├── OrderId (FK → Order)
├── FromStatus
├── ToStatus
├── ChangedByUserId (FK → User, nullable)
├── ChangedAt
├── Comment
├── IsSystemAction (bool)
└── CorrelationId (nullable)
```

### 1.4. Маршруты и доставка

```text
ROUTE
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── CourierId (FK → Courier)          # UNIQUE(CourierId, Date) WHERE IsDeleted = false
├── Date (DateOnly)
├── Status                            # Draft | Confirmed | InProgress | Completed | Cancelled
├── IsLocked (bool)
├── LockedAt (nullable)
├── PlannedStartTime (time)
├── TotalPoints (int)
├── TotalWeight (decimal)
├── TotalVolume (decimal)
├── TotalDistance (decimal)
├── TotalDuration (int, минуты)
├── Geometry (jsonb/polyline, nullable)
├── BuiltByAlgorithm (bool)
├── ConfirmedAt (nullable)
└── CancelledReason (nullable)
```
```text
ROUTE_POINT
├── Id (PK)
├── RouteId (FK → Route)
├── OrderId (FK → Order)              # уникальность только среди IsActive = true
├── SequenceNumber (int)
├── PlannedArrivalFrom (time)
├── PlannedArrivalTo (time)
├── ActualArrivalAt (nullable)
├── CompletedAt (nullable)
├── Status                            # Pending | Arrived | Delivered | Problem | Cancelled | Reassigned
├── IsActive (bool)
└── ReassignedToId (FK → RoutePoint, nullable)
```
```text
DELIVERY
├── Id (PK, Guid)
├── RoutePointId (FK → RoutePoint, 1:1)
├── OrderId (FK → Order)
├── CourierId (FK → Courier)
├── DeliveredAt (UTC)
├── AcceptedBy
├── SignatureFileId (FK → File, nullable)
├── HasProblem (bool)
├── ProblemId (FK → Problem, nullable)
├── ClientOperationId (Guid, UNIQUE)   # идемпотентность офлайна
├── ConfirmedAt (nullable)             # приёмка точкой / авто
└── Notes
```
```text
DELIVERY_PHOTO
├── Id (PK)
├── DeliveryId (FK → Delivery)
├── FileId (FK → File)
├── PhotoType            # Product | Act | Tare
└── UploadedAt
```
```text
PROBLEM
├── Id (PK, Guid)
├── OrderId (FK → Order)
├── CourierId (FK → Courier, nullable)
├── ReportedByUserId (FK → User)
├── Source               # Courier | PointManager | SalesManager | System
├── Type                 # PointClosed | Refused | NoTare | WrongAddress | Other
├── Comment
├── ResolvedByUserId (FK → User, nullable)
├── ResolvedAt (nullable)
├── Resolution           # Reassigned | Cancelled | ReturnedToWork | null
├── ReassignedToOrderId (FK → Order, nullable)
└── CreatedAt
```

### 1.5. Тара

```text
TARE
├── Id (PK)
├── TenantId (FK → Tenant)
├── Name
├── UnitWeight
└── IsActive
```
```text
TARE_MOVEMENT
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── TareId (FK → Tare)
├── OrderId (FK → Order)
├── CourierId (FK → Courier)
├── Quantity (int)
├── Direction            # Issued | Returned
├── IssuedMovementId (FK → TareMovement, nullable)  # связь возврата с выдачей
├── ClientOperationId (Guid, UNIQUE)
├── Comment
└── CreatedAt
```
```text
TARE_DEBT                 # сводка по курьеру/таре, пересчитывается триггером
├── Id (PK)
├── TenantId (FK → Tenant)
├── CourierId (FK → Courier)
├── TareId (FK → Tare)
├── IssuedQty (int)
├── ReturnedQty (int)
├── Debt (int)            # IssuedQty - ReturnedQty
└── UpdatedAt
```

### 1.6. Коммуникации

```text
CHAT_THREAD
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── Type                 # DispatcherCourier | DispatcherPoint
├── ParticipantAId (FK → User)
├── ParticipantBId (FK → User)
├── OrderId (FK → Order, nullable)
├── CreatedAt
└── LastMessageAt
```
```text
CHAT_MESSAGE
├── Id (PK, Guid)
├── ThreadId (FK → ChatThread)
├── SenderId (FK → User)
├── Text
├── AttachmentFileId (FK → File, nullable)
├── IsRead (bool)
├── CreatedAt
└── ReadAt (nullable)
```

### 1.7. Файлы

```text
FILE
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── OwnerType            # DeliveryPhoto | Signature | Act | Chat | Avatar
├── OwnerId (Guid)
├── StorageKey
├── MimeType
├── SizeBytes
├── Sha256
├── UploadedByUserId (FK → User)
├── RetentionUntil (nullable)
└── IsDeleted
```

### 1.8. Интеграции

```text
INTEGRATION_SETTINGS
├── Id (PK)
├── TenantId (FK → Tenant)
├── AmoClientId
├── AmoClientSecret (encrypted)
├── AmoSubdomain
├── AmoWebhookSecret (encrypted)
├── AmoPipelineId (long)
├── AmoStageMapJson (jsonb)          # маппинг стадий конкретной воронки
├── MapsProvider         # TwoGis | Yandex
├── MapsApiKey (encrypted)
├── EdoProvider          # Diadoc | Taxcom | Astral
├── EdoApiKey (encrypted)
├── IsAmoConnected (bool)
├── LastAmoSyncAt (nullable)
└── LastMapsCheckAt (nullable)
```
```text
AMO_LEAD_CACHE
├── Id (PK)
├── TenantId (FK → Tenant)
├── AmoLeadId (long)                 # UNIQUE(TenantId, AmoLeadId)
├── AmoPipelineId (long)
├── CurrentStageId (long)
├── CurrentStageName
├── ResponsibleAmoUserId (long)
├── CustomStatusFieldId (long)
├── LastSyncedAt
└── RawJson (jsonb)
```
```text
WEBHOOK_LOG
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── Source               # AmoCRM | Edo | Maps
├── EventType
├── ExternalEventId      # ключ идемпотентности
├── PayloadJson (jsonb)
├── SignatureValid (bool)
├── ProcessedAt (nullable)
├── ProcessingError (nullable)
├── WasDeferred (bool)
└── CreatedAt
```
```text
OUTBOX_EVENT               # исходящие вызовы (PATCH amoCRM, ЭДО, карты)
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── Type
├── PayloadJson (jsonb)
├── Attempts (int)
├── NextAttemptAt
├── LastError (nullable)
├── Status               # Pending | Processing | Sent | Failed
└── CreatedAt
```
```text
SCHEDULED_JOB              # отложенные уведомления, авто-подтверждения, дайджесты
├── Id (PK)
├── TenantId (FK → Tenant)
├── Type
├── EntityType, EntityId
├── RunAt
├── Status               # Pending | Done | Cancelled | Failed
├── Attempts
└── PayloadJson (jsonb)
```

### 1.9. Уведомления и аудит

```text
NOTIFICATION
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── UserId (FK → User)
├── Channel              # InApp | Push | Sms | Telegram | Email
├── Type
├── Title
├── Body
├── PayloadJson (jsonb)
├── DeliveryStatus       # Pending | Sent | Failed | Skipped
├── DedupeKey (nullable) # защита от дублей
├── IsRead (bool)
├── CreatedAt
└── ReadAt (nullable)
```
```text
AUDIT_LOG
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── UserId (FK → User, nullable)
├── Action               # Create | Update | Delete | StatusChange | Login | RouteLock | OrderDefer | ...
├── EntityType
├── EntityId (Guid)
├── OldValueJson (jsonb, nullable)
├── NewValueJson (jsonb, nullable)
├── IpAddress
├── UserAgent
├── CorrelationId
└── CreatedAt
```
```text
TARIFF
├── Id (PK)
├── TenantId (FK → Tenant)
├── Name
├── Purpose              # ClientPrice | CourierPay
├── Type                 # PerRoute | PerPoint | PerKm
├── Amount (numeric 18,2)
├── Currency
├── ValidFrom
├── ValidTo (nullable)
└── IsActive
```
```text
COURIER_PAYOUT
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── CourierId (FK → Courier)
├── PeriodFrom (DateOnly)
├── PeriodTo (DateOnly)
├── RoutesCount (int)
├── PointsCount (int)
├── Amount (numeric 18,2)
├── Status               # Draft | Approved | Paid
├── PaidAt (nullable)
└── Comment
```
```text
DOCUMENT
├── Id (PK, Guid)
├── TenantId (FK → Tenant)
├── OrderId (FK → Order)
├── EdoProvider
├── EdoDocumentId
├── Type                 # TransportWaybill | Act | Invoice
├── Status               # Draft | Sent | Signed | Rejected
├── SignedAt (nullable)
├── FileId (FK → File)
└── CreatedAt
```
```text
WORKING_CALENDAR
├── Id (PK)
├── TenantId (FK → Tenant)
├── Date (DateOnly)
├── IsWorking (bool)
└── Note
```

---

## Часть 2. Рабочий календарь и время

- `nextBusinessDay(from)` идёт вперёд и берёт первый день, где `WORKING_CALENDAR.IsWorking = true`; если календарь пуст — пропускает сб/вс.
- Планировщик (`SCHEDULED_JOB`, Quartz/Hangfire) выполняет:
  - в `PlanningLockTime` — блокировка маршрутов на сегодня + рассылка;
  - в `DeliveryStartTime` — push курьерам «Можно забирать»;
  - в `DeliveryStartTime + grace` — проверка «не выехал»;
  - каждую минуту — отложенные уведомления и авто-подтверждения;
  - раз в день — дайджест руководителю.
- Все расчёты окон — в `Tenant.TimeZone`, хранение — UTC.

---

## Часть 3. Статусная модель

### 3.1. Канонический enum `Order.Status`

`New | Scheduled | Accepted | InTransit | Delivered | Completed | Problem | Cancelled | Rejected`

- `Rejected` — диспетчер отклонил заказ при приёме.
- `ValidationError = InvalidDate` — это **флаг**, а не статус; заказ остаётся `New` и не планируется.

### 3.2. Переходы

| Из | В | Условие / кто |
|---|---|---|
| New | Scheduled | Диспетчер добавил в маршрут; `Route.IsLocked = false`; нет `IsBlockedByPointApproval`, нет `ValidationError` |
| New | Rejected | Диспетчер отклонил с причиной |
| New | Cancelled | Менеджер/диспетчер отменил |
| Scheduled | Accepted | Курьер нажал «Принять маршрут» |
| Scheduled | Cancelled | Отмена до старта |
| Accepted | InTransit | Курьер «Забрал в цехе» (>= `DeliveryStartTime`, внутри геофенса) |
| Accepted | Cancelled | Отмена до выезда |
| InTransit | Delivered | Курьер «Доставлено» |
| InTransit | Problem | Курьер сообщил проблему |
| InTransit | Cancelled | Отмена в пути (BR-11), требует ack курьера |
| Delivered | Completed | Точка подтвердила / авто через `AutoConfirmDeliveryAfterMinutes` |
| Delivered | Problem | Проблема обнаружена до подтверждения |
| Completed | Problem | Спор/возврат в течение `ReopenDeliveryWindowDays` (Head/Admin) |
| Problem | New | `Resolution = Reassigned` → новая дата (next business day) |
| Problem | Scheduled | `Resolution = ReturnedToWork` и маршрут не заблокирован |
| Problem | Cancelled | `Resolution = Cancelled` |

### 3.3. `Route.Status`

`Draft → Confirmed → InProgress → Completed`, ветка `→ Cancelled`.

- `Confirmed` — маршрут утверждён, но **редактируем до `IsLocked`**.
- `InProgress` ставится автоматически при первом «Забрал в цехе» по маршруту.
- `Completed` — все точки в терминальном статусе.

### 3.4. `RoutePoint.Status`

`Pending | Arrived | Delivered | Problem | Cancelled | Reassigned`

- `Arrived` — геофенс/ручное «На месте» (опционально).
- `Reassigned` — при переназначении; старая запись `IsActive = false`, `ReassignedToId` указывает на новую.

### 3.5. Маппинг в amoCRM (`AmoStageMapJson`)

| Наш статус | Стадия amoCRM (пример) |
|---|---|
| New (без флагов) | «Готов к доставке» |
| New + `IsDeferred` | «Перенесён на след. день» |
| New + `ValidationError` | «Требует уточнения даты» |
| Scheduled | «Доставка запланирована» |
| Accepted | «Курьер назначен» |
| InTransit | «Доставка в пути» |
| Delivered | «Доставлено» |
| Completed | «Успешно реализовано» |
| Problem | «Проблема доставки» |
| Cancelled / Rejected | «Закрыто и не реализовано» |

Исходящие PATCH идут через `OUTBOX_EVENT` (retry, backoff, rate-limit).

---

## Часть 4. Роли, экраны, кнопки

### Роль 1. Менеджер продаж (`SalesManager`) — amoCRM + мини-панель `/manager`

- В amoCRM: перетаскивание сделки на «Готов к доставке» → вебхук → `Order`. Видит кастомное поле «Статус доставки».
- Мини-панель (адаптивный Blazor):
  - **Экран 1.1 «Мои заказы»** — заказы, где `SalesManagerUserId` = текущий (маппинг через `AMO_USER_MAP`). Статус в реальном времени (SignalR), фото/подпись после доставки. Кнопка «Сообщить о проблеме».
  - **Экран 1.2 «Мои точки»** — список своих точек; «Добавить», «Редактировать» (только свои), «Деактивировать» (только свои).
  - **Форма точки**: название, адрес с автокомплитом, мини-карта с перетаскиваемым маркером, телефон с маской, контактное лицо, окно доставки, комментарий; «Проверить на дубликат»; «Сохранить»; «Отмена».
- Уведомления: `OrderDeferred`, `PointApproved`, `PointRejected`, `PointDuplicateSuspected`, `ProblemReported`.

### Роль 2. Диспетчер (`Dispatcher`) — Blazor Web

- **2.1 «Входящие заказы»** — группировка по `RequestedDate`; бейджи «Сегодня/Завтра/Перенесён/Ошибка даты/Точка на модерации». Кнопки: «Назначить» (неактивна для заблокированных), «Отклонить», «Подробнее», «Массовое назначение».
- **2.2 «Планирование маршрутов»** — карта, панель курьеров (рейтинг, точки, вес, лимиты), «Автомаршрут» (VRP: вес/объём/окна/лимиты), «Подтвердить», «Очистить день». После `PlanningLockTime` — read-only для сегодня, по умолчанию «Завтра», плашка «Заблокировано в 10:00».
- **2.3 «Трекинг онлайн»** — SignalR-карта, статус/ETA/офлайн-индикаторы, «Позвонить/Написать», чат с быстрыми ответами.
- **2.4 «Проблемные заказы»** — «Переназначить» (на будущую дату, если заблокировано), «Отменить», «Вернуть в работу», «Связаться с точкой».

### Роль 3. Курьер (`Courier`) — .NET MAUI, offline-first

- **3.1 Вход** — телефон + OTP (`OTP_CODE`, cooldown, лимит попыток); регистрация по приглашению.
- **3.2 «Мои маршруты»** — карточки на сегодня/завтра из локального кэша; «Открыть»; «Я не смогу» (снимает с маршрута, возвращает заказы в пул, уведомляет диспетчера); индикатор синхронизации.
- **3.3 «Маршрут»** — упорядоченные точки; «Навигация», «Позвонить», «Проблема»; «Забрал в цехе» (активна только с `DeliveryStartTime` + геофенс); pull-to-refresh.
- **3.4 «Подтверждение доставки»** — чек-лист, фото, подпись, «Кто принял», QR-сканирование; «Доставлено» → `Delivery` с `ClientOperationId`.
- **3.5 «Проблема»** — тип, комментарий, отправка.
- **3.6 «Возврат тары»** — типы, количество, «Зафиксировать» → `TareMovement` + пересчёт `TARE_DEBT`.
- Уведомления: назначение маршрута, чат, «Подтвердите» (см. разрешение ниже), отмена, офлайн-предупреждение, старт доставки.

> **Разрешение противоречия «Подтвердите заказ №123»:** напоминание через 30 мин после `Delivered` уходит **менеджеру точки** (подтвердить приёмку). Курьеру — только если он не завершил чек-лист/подпись (статус `Delivered` без `SignatureFileId`).

### Роль 4. Менеджер точки (`PointManager`) — PWA

- Учётка привязывается через `POINT.UserId`. Вход — телефон + пароль/OTP.
- **4.1 «Ожидаемые поставки»** — сегодня/завтра, статус, «Подтвердить получение» (→ `Completed`), «Сообщить о проблеме» (`Problem.Source = PointManager`).
- **4.2 «История»** — завершённые поставки, скачивание актов.
- Уведомления: «Курьер в пути, через 15 мин», «Поставка подтверждена, акт доступен», «Подтвердите получение».

### Роль 5. Администратор (`Admin`) — Blazor Web

- **5.1 «Пользователи и роли»** — создание, блокировка, сброс пароля, смена роли; приглашение курьеров.
- **5.2 «Справочники»** — «Точки» (колонки Source/CreatedBy/ApprovalStatus, «Одобрить/Отклонить/История»), «Курьеры», «Тарифы», «Тара», «Импорт Excel».
- **5.3 «Настройки интеграций»** — amoCRM (ClientId/Secret, воронка, «Проверить подключение», статус вебхука), карты (провайдер, ключ, «Тест»), ЭДО, **«Временные окна и лимиты»** (все параметры Части 0 + `RequirePointApprovalForManager`).
- **5.4 «Журнал аудита»** — фильтры + вкладка «Автодействия».
- **5.5 «Модерация точек»** — если включена модерация (её же видит Head).
- Уведомления: сбой вебхука, ошибка карт, блокировка маршрутов, новая точка от менеджера.

### Роль 6. Руководитель (`Head`) — Blazor Web

- **6.1 «Оперативный дашборд»** — KPI, графики, тепловая карта проблем, топ курьеров, виджет «Перенесённые заказы», кнопка **«Добавить точку»**, виджет «Точки на модерации». Экспорт в Excel (асинхронный отчёт).
- **6.2 «Финансы»** — `COURIER_PAYOUT` + тарифы, экспорт.
- **6.3 «Модерация точек»** — одобрение/отклонение.
- **6.4 «Все точки»** — полный список, фильтры по Source/ApprovalStatus/IsActive.
- Дайджест раз в день.

---

## Часть 5. Матрица уведомлений

| Событие | Кому | Канал | Текст |
|---|---|---|---|
| Новый заказ | Диспетчер | InApp+Push | «Новый заказ №124 (на завтра)» |
| Заказ перенесён по cutoff | Диспетчер + SalesManager | Push+Telegram | «Заказ №123 перенесён на завтра: cutoff 10:00» |
| Невалидная дата | SalesManager | Telegram | «Заказ №125: дата в прошлом» |
| Менеджер создал точку | Admin + Head | InApp+Telegram | «Иванов добавил точку "Кафе Уют"» |
| Точка на модерации | Admin + Head | InApp+Push | «"Кафе Уют" ожидает одобрения» |
| Точка одобрена/отклонена | SalesManager-автор | InApp+Push | «"Кафе Уют" одобрена» / «отклонена: причина» |
| Заказ на точку на модерации | Диспетчер | InApp | «Заказ №124: точка на модерации» |
| Маршрут назначен | Курьер | Push | «Маршрут на завтра: 12 точек» |
| Маршрут принят | Диспетчер | InApp | «Петров принял маршрут» |
| Маршруты заблокированы | Диспетчер + Admin | InApp | «10:00 — маршруты на сегодня заблокированы» |
| Старт доставки | Курьер | Push | «Можно забирать заказы в цехе» |
| Курьер не выехал | Диспетчер | InApp | «Петров не забрал заказы» |
| Забрал в цехе | Диспетчер | InApp | обновление статуса |
| В пути | Точка | Push/SMS | «Курьер будет через 15 мин» |
| Доставлено | Диспетчер + amoCRM | InApp+PATCH | «Заказ №123 доставлен» |
| Приёмка не подтверждена | PointManager | Push | «Подтвердите получение №123» |
| Проблема | Диспетчер + SalesManager | Push+Telegram | «Точка закрыта (№123)» |
| Офлайн > 10 мин | Диспетчер | InApp | «Петров офлайн 15 мин» |
| Отклонение > 2 км | Диспетчер | InApp | «Отклонился на 2 км» |
| Отмена | Диспетчер+Курьер | Push | «Заказ №124 отменён» |
| Ошибка интеграции | Admin | Email/Telegram | «Вебхук amoCRM не получен 30 мин» |
| Дайджест | Head | Telegram/Email | «280 доставок, 96% в срок, 4 проблемы, 6 переносов» |

Правила: `DedupeKey` не даёт дублей; ночные push — только `InApp`; `SCHEDULED_JOB` отвечает за отложенные.

---

## Часть 6. Офлайн-синхронизация (MAUI ↔ сервер)

1. **Локальное хранилище** — SQLite с теми же ключами (`Id` = GUID, генерируется на клиенте).
2. **Очередь операций** — каждая операция: `{ ClientOperationId, Type, Payload, CreatedAt, Attempts, Status }`.
3. **Идемпотентность** — сервер делает upsert по `ClientOperationId` (уникальные индексы в `Delivery`, `TareMovement`), повтор не создаёт дубль.
4. **Delta pull** — клиент запрашивает `GET /sync?since=<cursor>`; сервер отдаёт изменения из change feed и **tombstones**. Курсор — непрозрачный серверный (не `lastPulledAt`: часы и одинаковые timestamps ломают выборку).
5. **Разрешение конфликтов**:
   - статусы доставки — server-wins по версии записи (`RowVersion` / `Order.Version`); при конфликте `409` клиент повторяет pull и показывает расхождение;
   - фото/подпись — append-only (файл нельзя перезаписать, только добавить);
   - отметки «прочитано», «принял маршрут» — merge по `max`.
6. **Порядок отправки** — родитель → потомок: `Order status → Delivery → DeliveryPhoto → TareMovement → Problem`.
7. **Статусы UI** — 🟢 `Synced`, 🟡 `Pending`, 🔴 `Failed` (с причиной и кнопкой «Повторить»).
8. **Обнаружение «офлайн > N»** — сервер по `USER_DEVICE.LastSeenAt` и последнему `sync`; уведомление диспетчеру.

> Реализационные детали (change feed, идемпотентный push, версия записи) — ADR-0007.

---

## Часть 7. Сквозные сценарии и принятые решения

### 7.1. День (с учётом 10:00)

- **08:00** — 50 сделок на «сегодня» до cutoff → `Order` `New`, `IsDeferred = false`. Диспетчеру 50 тостов.
- **08:15** — «Массовое назначение» → Петров → «Автомаршрут» (VRP с лимитами) → «Подтвердить» (`Confirmed`). Push курьеру.
- **08:30–09:50** — новые заказы «на сегодня» принимаются, добавляются в незаблокированный маршрут.
- **10:00** — `PlanningLockTime`: маршруты на сегодня `IsLocked`. Диспетчеру и админу — 🔒. Курьеру — ℹ️ «Можно забирать». Экран планирования → «Завтра».
- **10:05** — Петров «Забрал в цехе» (геофенс) → `InTransit`, маршрут `InProgress`.
- **10:20** — новый заказ «на сегодня» после cutoff → `IsDeferred = true`, `RequestedDate = nextBusinessDay`. Уведомления менеджеру и диспетчеру.
- **10:20–13:00** — по точкам: «Доставлено» → чек-лист → фото → подпись; офлайн — в SQLite, затем sync.
- **13:10** — «Точка закрыта» → `Problem`, push диспетчеру; «Переназначить» — маршрут заблокирован → на завтра (`Reassigned`).
- **15:00** — менеджер отменяет сделку, заказ `InTransit` → `Cancelled` (BR-11), курьеру push, `RoutePoint = Cancelled`.
- **18:00** — всё `Completed`, статусы через `OUTBOX_EVENT` улетели в amoCRM, Head получил дайджест.

### 7.2. Принятые решения по открытым вопросам

1. **Один лид amoCRM = один заказ** (MVP), `UNIQUE(TenantId, AmoLeadId)`; расширение на мультидоставку — отдельный `AmoLeadItemIndex`.
2. **`NoTare`** — тип проблемы, который дополнительно создаёт/учитывает долг в `TARE_DEBT`.
3. **Приёмку подтверждает точка**; при отсутствии действия — авто-подтверждение через `AutoConfirmDeliveryAfterMinutes`.
4. **Тарифы разделены на `ClientPrice` и `CourierPay`**; расчёт выплат — `COURIER_PAYOUT`.
5. **Спор после `Completed`** разрешён в течение `ReopenDeliveryWindowDays` только Head/Admin.
6. **Менеджер продаж не создаёт заказы напрямую** — только через amoCRM (флаг для будущего расширения).
7. **Один депо на тенант** (MVP); модель готова к `WAREHOUSE` при необходимости.

---

## Часть 8. Нефункциональные требования (кратко)

- **Безопасность**: RBAC по ролям, tenant isolation на уровне запросов, шифрование секретов интеграций, OTP с rate-limit, пароли — Argon2id.
- **Аудит**: `AUDIT_LOG` append-only, `CorrelationId` сквозной, ретенция по политике.
- **Файлы**: `FILE` с антивирусом, лимитами размера, ретенцией (подписи/фото — по договору/152-ФЗ).
- **Надёжность**: `OUTBOX_EVENT` и `SCHEDULED_JOB` с retry/backoff, идемпотентные вебхуки.
- **Производительность**: PostGIS-GIST по точкам, индексы по `(TenantId, Status, RequestedDate)`, `(CourierId, Date)`, `UpdatedAt` для delta sync.
- **Наблюдаемость**: health-checks, метрики очередей исходящих событий, алерты по сбоям вебхуков.
