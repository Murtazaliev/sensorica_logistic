using System.Text.Json;
using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Sync;
using DeliverySystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliverySystem.Infrastructure.Sync;

public sealed class SyncService(AppDbContext db) : ISyncService
{
    public async Task<SyncPushResult> PushAsync(
        Guid tenantId,
        SyncPushRequest request,
        CancellationToken ct = default)
    {
        var results = new List<SyncOperationResult>(request.Operations.Count);

        foreach (var operation in request.Operations)
        {
            results.Add(await ApplyAsync(tenantId, request.DeviceId, operation, ct));
        }

        return new SyncPushResult(results);
    }

    public async Task<SyncPullResult> PullAsync(
        Guid tenantId,
        long since,
        int limit,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 500);

        var rows = await db.SyncChanges
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id > since)
            .OrderBy(x => x.Id)
            .Take(limit + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var changes = rows
            .Select(x => new SyncChangeDto(
                x.Id,
                x.EntityType,
                x.EntityId,
                x.ChangeType,
                x.PayloadJson,
                x.ChangedAt))
            .ToList();

        var nextCursor = changes.Count == 0 ? since : changes[^1].Cursor;
        return new SyncPullResult(changes, nextCursor, hasMore);
    }

    public async Task<long> AppendTombstoneAsync(
        Guid tenantId,
        SyncEntityType entityType,
        Guid entityId,
        DateTimeOffset changedAt,
        CancellationToken ct = default)
    {
        var change = new SyncChange
        {
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            ChangeType = SyncChangeType.Tombstone,
            PayloadJson = "{}",
            ChangedAt = changedAt
        };

        db.SyncChanges.Add(change);
        await db.SaveChangesAsync(ct);
        return change.Id;
    }

    private async Task<SyncOperationResult> ApplyAsync(
        Guid tenantId,
        string deviceId,
        SyncPushOperation operation,
        CancellationToken ct)
    {
        var existing = await FindOperationAsync(tenantId, operation.ClientOperationId, ct);
        if (existing is not null)
        {
            return FromStored(existing);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var outcome = operation.Type switch
        {
            SyncOperationType.SetOrderStatus => await ApplySetOrderStatusAsync(tenantId, operation, now, ct),
            _ => new Outcome(SyncOperationStatus.Rejected, null, "unsupported_operation")
        };

        db.SyncOperations.Add(new SyncOperation
        {
            TenantId = tenantId,
            DeviceId = deviceId,
            ClientOperationId = operation.ClientOperationId,
            Type = operation.Type,
            PayloadJson = operation.PayloadJson,
            Status = outcome.Status,
            ResultJson = outcome.ResultJson,
            Error = outcome.Error,
            ClientCreatedAt = operation.CreatedAt,
            AppliedAt = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return outcome.ToResult(operation.ClientOperationId);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();

            var winner = await FindOperationAsync(tenantId, operation.ClientOperationId, ct)
                ?? throw new InvalidOperationException("Sync operation insert failed without a stored winner.");

            return FromStored(winner);
        }
    }

    private async Task<Outcome> ApplySetOrderStatusAsync(
        Guid tenantId,
        SyncPushOperation operation,
        DateTimeOffset now,
        CancellationToken ct)
    {
        SetOrderStatusPayload? payload;
        try
        {
            payload = SyncJson.Deserialize<SetOrderStatusPayload>(operation.PayloadJson);
        }
        catch (JsonException)
        {
            return new Outcome(SyncOperationStatus.Rejected, null, "invalid_payload");
        }

        if (payload is null)
        {
            return new Outcome(SyncOperationStatus.Rejected, null, "invalid_payload");
        }

        var order = await db.Orders.FirstOrDefaultAsync(
            x => x.Id == payload.OrderId && x.TenantId == tenantId, ct);

        if (order is null)
        {
            return new Outcome(SyncOperationStatus.Rejected, null, "order_not_found");
        }

        if (order.Version != payload.ExpectedVersion)
        {
            return new Outcome(
                SyncOperationStatus.Conflict,
                Snapshot(order),
                $"version_conflict: server={order.Version}, client={payload.ExpectedVersion}");
        }

        if (order.Status == payload.ToStatus)
        {
            return new Outcome(SyncOperationStatus.Applied, Snapshot(order), null);
        }

        if (!OrderStatusTransitions.IsAllowed(order.Status, payload.ToStatus))
        {
            return new Outcome(
                SyncOperationStatus.Rejected,
                Snapshot(order),
                $"invalid_transition: {order.Status}->{payload.ToStatus}");
        }

        var fromStatus = order.Status;
        order.Status = payload.ToStatus;
        order.Version++;
        order.UpdatedAt = now;

        db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = fromStatus,
            ToStatus = payload.ToStatus,
            ChangedAt = now,
            IsSystemAction = false,
            Comment = string.IsNullOrWhiteSpace(payload.Comment) ? "Синхронизация" : payload.Comment,
            CorrelationId = operation.ClientOperationId.ToString()
        });

        db.SyncChanges.Add(SyncChangeFactory.ForOrder(order, SyncChangeType.Upsert, now));

        return new Outcome(SyncOperationStatus.Applied, Snapshot(order), null);
    }

    private Task<SyncOperation?> FindOperationAsync(Guid tenantId, Guid clientOperationId, CancellationToken ct)
    {
        return db.SyncOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.ClientOperationId == clientOperationId, ct);
    }

    private static SyncOperationResult FromStored(SyncOperation stored) => new(
        stored.ClientOperationId,
        stored.Status,
        true,
        stored.Error,
        stored.ResultJson);

    private static string Snapshot(Order order) => SyncJson.Serialize(new OrderSyncSnapshot(
        order.Id,
        order.OrderNumber,
        order.Status,
        order.Version,
        order.PointId,
        order.RequestedDate,
        order.UpdatedAt));

    private sealed record Outcome(SyncOperationStatus Status, string? ResultJson, string? Error)
    {
        public SyncOperationResult ToResult(Guid clientOperationId) => new(
            clientOperationId,
            Status,
            false,
            Error,
            ResultJson);
    }
}
