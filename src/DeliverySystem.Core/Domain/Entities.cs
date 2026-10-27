using System.Text.Json.Serialization;

namespace DeliverySystem.Core.Domain;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Europe/Moscow";
    public TimeOnly DeliveryStartTime { get; set; } = new(10, 0);
    public TimeOnly CutoffTimeForTodayOrders { get; set; } = new(10, 0);
    public TimeOnly PlanningLockTime { get; set; } = new(10, 0);
    public bool IsActive { get; set; } = true;
}

public sealed class Point
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public long? ExternalId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public PointApprovalStatus ApprovalStatus { get; set; } = PointApprovalStatus.Active;
    public bool IsActive { get; set; } = true;
}

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long? AmoLeadId { get; set; }
    public long AmoStageId { get; set; }
    public Guid? PointId { get; set; }
    public Guid? SalesManagerUserId { get; set; }
    public DateOnly RequestedDate { get; set; }
    public DateOnly OriginalRequestedDate { get; set; }
    public bool IsDeferred { get; set; }
    public OrderDeferredReason? DeferredReason { get; set; }
    public OrderValidationError? ValidationError { get; set; }
    public bool IsBlockedByPointApproval { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.New;
    public decimal TotalWeight { get; set; }
    public decimal TotalVolume { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "RUB";
    public string? Comment { get; set; }
    public string? CancelledReason { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<OrderItem> Items { get; set; } = [];
    public List<OrderStatusHistory> StatusHistory { get; set; } = [];

    [JsonIgnore]
    public Point? Point { get; set; }
}

public sealed class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "kg";
    public decimal Weight { get; set; }
    public string? Comment { get; set; }
}

public sealed class OrderStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Comment { get; set; }
    public bool IsSystemAction { get; set; } = true;
    public string? CorrelationId { get; set; }
}

public sealed class WebhookLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public WebhookSource Source { get; set; } = WebhookSource.AmoCRM;
    public string EventType { get; set; } = string.Empty;
    public string ExternalEventId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public bool SignatureValid { get; set; } = true;
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? ProcessingError { get; set; }
    public bool WasDeferred { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
