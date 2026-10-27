using System.Text.Json;
using System.Text.Json.Serialization;
using DeliverySystem.Core.Domain;

namespace DeliverySystem.Core.Sync;

public sealed record SyncPushOperation(
    Guid ClientOperationId,
    SyncOperationType Type,
    string PayloadJson,
    DateTimeOffset CreatedAt);

public sealed record SyncPushRequest(string DeviceId, List<SyncPushOperation> Operations);

public sealed record SyncOperationResult(
    Guid ClientOperationId,
    SyncOperationStatus Status,
    bool IsDuplicate,
    string? Error,
    string? ResultJson);

public sealed record SyncPushResult(IReadOnlyList<SyncOperationResult> Results);

public sealed record SyncChangeDto(
    long Cursor,
    SyncEntityType EntityType,
    Guid EntityId,
    SyncChangeType ChangeType,
    string PayloadJson,
    DateTimeOffset ChangedAt);

public sealed record SyncPullResult(IReadOnlyList<SyncChangeDto> Changes, long NextCursor, bool HasMore);

public sealed record SetOrderStatusPayload(
    Guid OrderId,
    OrderStatus ToStatus,
    long ExpectedVersion,
    string? Comment = null);

public sealed record OrderSyncSnapshot(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    long Version,
    Guid? PointId,
    DateOnly RequestedDate,
    DateTimeOffset UpdatedAt);

public sealed record PointSyncSnapshot(
    Guid Id,
    string Name,
    string Address,
    string? Phone,
    long? ExternalId,
    double? Latitude,
    double? Longitude,
    PointApprovalStatus ApprovalStatus,
    bool IsActive);

public interface ISyncService
{
    Task<SyncPushResult> PushAsync(Guid tenantId, SyncPushRequest request, CancellationToken ct = default);

    Task<SyncPullResult> PullAsync(Guid tenantId, long since, int limit, CancellationToken ct = default);

    Task<long> AppendTombstoneAsync(
        Guid tenantId,
        SyncEntityType entityType,
        Guid entityId,
        DateTimeOffset changedAt,
        CancellationToken ct = default);
}

public static class SyncJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

public static class SyncChangeFactory
{
    public static SyncChange ForOrder(Order order, SyncChangeType changeType, DateTimeOffset changedAt) => new()
    {
        TenantId = order.TenantId,
        EntityType = SyncEntityType.Order,
        EntityId = order.Id,
        ChangeType = changeType,
        PayloadJson = SyncJson.Serialize(new OrderSyncSnapshot(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.Version,
            order.PointId,
            order.RequestedDate,
            order.UpdatedAt)),
        ChangedAt = changedAt
    };

    public static SyncChange ForPoint(Point point, SyncChangeType changeType, DateTimeOffset changedAt) => new()
    {
        TenantId = point.TenantId,
        EntityType = SyncEntityType.Point,
        EntityId = point.Id,
        ChangeType = changeType,
        PayloadJson = SyncJson.Serialize(new PointSyncSnapshot(
            point.Id,
            point.Name,
            point.Address,
            point.Phone,
            point.ExternalId,
            point.Latitude,
            point.Longitude,
            point.ApprovalStatus,
            point.IsActive)),
        ChangedAt = changedAt
    };
}
