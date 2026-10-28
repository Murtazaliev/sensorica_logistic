using System.Text.Json.Serialization;

namespace DeliverySystem.Core.Domain;

public enum CourierType
{
    Staff,
    Contractor,
    SelfEmployed
}

public enum VehicleType
{
    Car,
    Motorcycle,
    Bicycle,
    OnFoot
}

public enum RouteStatus
{
    Draft,
    Confirmed,
    InProgress,
    Completed,
    Cancelled
}

public enum RoutePointStatus
{
    Pending,
    Arrived,
    Delivered,
    Problem,
    Cancelled,
    Reassigned
}

public sealed class Courier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public CourierType Type { get; set; } = CourierType.Staff;
    public VehicleType VehicleType { get; set; } = VehicleType.Car;
    public string? VehiclePlate { get; set; }
    public decimal CapacityWeight { get; set; } = 500m;
    public decimal CapacityVolume { get; set; } = 1m;
    public int? MaxPointsPerDay { get; set; }
    public double Rating { get; set; } = 5;
    public bool IsAvailable { get; set; } = true;
    public string? BlockedReason { get; set; }

    [JsonIgnore]
    public AppUser? User { get; set; }
}

public sealed class Route
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CourierId { get; set; }
    public DateOnly Date { get; set; }
    public RouteStatus Status { get; set; } = RouteStatus.Draft;
    public bool IsLocked { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public TimeOnly PlannedStartTime { get; set; } = new(10, 0);
    public int TotalPoints { get; set; }
    public decimal TotalWeight { get; set; }
    public decimal TotalVolume { get; set; }
    public decimal TotalDistance { get; set; }
    public int TotalDuration { get; set; }
    public string? Geometry { get; set; }
    public bool BuiltByAlgorithm { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public string? CancelledReason { get; set; }

    [JsonIgnore]
    public Courier? Courier { get; set; }

    public List<RoutePoint> Points { get; set; } = [];
}

public sealed class RoutePoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RouteId { get; set; }
    public Guid OrderId { get; set; }
    public int SequenceNumber { get; set; }
    public TimeOnly? PlannedArrivalFrom { get; set; }
    public TimeOnly? PlannedArrivalTo { get; set; }
    public DateTimeOffset? ActualArrivalAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RoutePointStatus Status { get; set; } = RoutePointStatus.Pending;
    public bool IsActive { get; set; } = true;
    public Guid? ReassignedToId { get; set; }

    [JsonIgnore]
    public Order? Order { get; set; }
}
