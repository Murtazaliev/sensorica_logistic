namespace DeliverySystem.Core.Domain;

public enum SyncOperationType
{
    SetOrderStatus
}

public enum SyncOperationStatus
{
    Applied,
    Rejected,
    Conflict
}

public enum SyncChangeType
{
    Upsert,
    Tombstone
}

public enum SyncEntityType
{
    Order,
    Point,
    Delivery,
    TareMovement,
    Problem
}

public sealed class SyncOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public Guid ClientOperationId { get; set; }
    public SyncOperationType Type { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public SyncOperationStatus Status { get; set; }
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset ClientCreatedAt { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}

public sealed class SyncChange
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public SyncEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public SyncChangeType ChangeType { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset ChangedAt { get; set; }
}
