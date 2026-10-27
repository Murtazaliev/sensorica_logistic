using DeliverySystem.Core.Domain;

namespace DeliverySystem.Core.Points;

public sealed record PointUpsertRequest(
    Guid? Id,
    string Name,
    string Address,
    string? Phone,
    long? ExternalId,
    double? Latitude,
    double? Longitude,
    PointApprovalStatus ApprovalStatus);

public sealed record PointUpsertResult(
    Point Point,
    int UnblockedOrdersCount);
