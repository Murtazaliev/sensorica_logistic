using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Intake;

namespace DeliverySystem.Api.Components;

public static class StatusDisplay
{
    public static string Label(OrderStatus status) => status switch
    {
        OrderStatus.New => "Новый",
        OrderStatus.Scheduled => "Запланирован",
        OrderStatus.Accepted => "Принят",
        OrderStatus.InTransit => "В пути",
        OrderStatus.Delivered => "Доставлен",
        OrderStatus.Completed => "Завершён",
        OrderStatus.Problem => "Проблема",
        OrderStatus.Cancelled => "Отменён",
        OrderStatus.Rejected => "Отклонён",
        _ => status.ToString()
    };

    public static string CssClass(OrderStatus status) => "status-" + status.ToString().ToLowerInvariant();

    public static string Label(PointApprovalStatus status) => status switch
    {
        PointApprovalStatus.Active => "Активна",
        PointApprovalStatus.PendingApproval => "На модерации",
        PointApprovalStatus.Rejected => "Отклонена",
        _ => status.ToString()
    };

    public static string CssClass(PointApprovalStatus status) => status switch
    {
        PointApprovalStatus.Active => "status-delivered",
        PointApprovalStatus.PendingApproval => "status-intransit",
        PointApprovalStatus.Rejected => "status-rejected",
        _ => "status-cancelled"
    };

    public static string HistoryComment(string? comment) => comment switch
    {
        "Order intake" => "Создан из amoCRM",
        "Cancelled via amoCRM" => "Отменён из amoCRM",
        "Courier sync" => "Синхронизация",
        _ => comment ?? string.Empty
    };

    public static string Label(SyncEntityType type) => type switch
    {
        SyncEntityType.Order => "Заказ",
        SyncEntityType.Point => "Точка",
        SyncEntityType.Route => "Маршрут",
        SyncEntityType.Delivery => "Доставка",
        SyncEntityType.TareMovement => "Тара",
        SyncEntityType.Problem => "Проблема",
        _ => type.ToString()
    };

    public static string Label(SyncChangeType type) => type switch
    {
        SyncChangeType.Upsert => "Обновление",
        SyncChangeType.Tombstone => "Удаление",
        _ => type.ToString()
    };

    public static string Label(SyncOperationType type) => type switch
    {
        SyncOperationType.SetOrderStatus => "Смена статуса",
        SyncOperationType.AcceptRoute => "Принятие маршрута",
        SyncOperationType.StartRoute => "Старт маршрута",
        SyncOperationType.ArriveRoutePoint => "Прибытие на точку",
        SyncOperationType.DeliverRoutePoint => "Доставка точки",
        SyncOperationType.ProblemRoutePoint => "Проблема на точке",
        _ => type.ToString()
    };

    public static string Label(SyncOperationStatus status) => status switch
    {
        SyncOperationStatus.Applied => "Применено",
        SyncOperationStatus.Conflict => "Конфликт",
        SyncOperationStatus.Rejected => "Отклонено",
        _ => status.ToString()
    };

    public static string Label(IntakeStatus status) => status switch
    {
        IntakeStatus.Created => "Создан",
        IntakeStatus.Updated => "Обновлён",
        IntakeStatus.Deferred => "Перенесён после 10:00",
        IntakeStatus.Cancelled => "Отменён",
        IntakeStatus.Duplicate => "Дубликат (пропущен)",
        IntakeStatus.ValidationError => "Ошибка валидации",
        _ => status.ToString()
    };

    public static string DeviceLabel(string? deviceId) => deviceId switch
    {
        null or "" => "—",
        "dispatcher-web" => "Кабинет диспетчера",
        "demo-device" => "Демо-устройство курьера",
        "smoke-dispatcher" => "Автопроверка",
        "string" => "Swagger (шаблон по умолчанию)",
        _ => deviceId
    };

    public static string OperationError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "—";
        }

        if (error == "invalid_payload")
        {
            return "Некорректные данные операции";
        }

        if (error == "order_not_found")
        {
            return "Заказ не найден";
        }

        if (error == "unsupported_operation")
        {
            return "Неподдерживаемый тип операции";
        }

        if (error.StartsWith("version_conflict", StringComparison.OrdinalIgnoreCase))
        {
            return "Конфликт версий заказа";
        }

        if (error.StartsWith("invalid_transition", StringComparison.OrdinalIgnoreCase))
        {
            return "Недопустимый переход статуса";
        }

        return error;
    }
}
