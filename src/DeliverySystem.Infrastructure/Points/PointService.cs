using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Points;
using DeliverySystem.Core.Sync;
using DeliverySystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliverySystem.Infrastructure.Points;

public sealed class PointService(AppDbContext db)
{
    public async Task<IReadOnlyList<Point>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await db.Points
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<PointUpsertResult> UpsertAsync(
        Guid tenantId,
        PointUpsertRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Укажите название точки.");
        }

        if (string.IsNullOrWhiteSpace(request.Address))
        {
            throw new ArgumentException("Укажите адрес точки.");
        }

        if (request.Latitude is < -90 or > 90)
        {
            throw new ArgumentException("Широта должна быть в диапазоне от -90 до 90.");
        }

        if (request.Longitude is < -180 or > 180)
        {
            throw new ArgumentException("Долгота должна быть в диапазоне от -180 до 180.");
        }

        if (request.ExternalId is not null)
        {
            var duplicateExternal = await db.Points.AnyAsync(
                p => p.TenantId == tenantId
                     && p.ExternalId == request.ExternalId
                     && p.Id != (request.Id ?? Guid.Empty),
                ct);

            if (duplicateExternal)
            {
                throw new InvalidOperationException(
                    $"Точка с внешним ID amoCRM {request.ExternalId} уже существует.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        Point? point = null;

        if (request.Id is not null && request.Id != Guid.Empty)
        {
            point = await db.Points.FirstOrDefaultAsync(
                p => p.Id == request.Id && p.TenantId == tenantId, ct);
        }

        if (point is null)
        {
            point = new Point
            {
                Id = request.Id is not null && request.Id != Guid.Empty ? request.Id.Value : Guid.NewGuid(),
                TenantId = tenantId
            };
            db.Points.Add(point);
        }

        point.Name = request.Name.Trim();
        point.Address = request.Address.Trim();
        point.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        point.ExternalId = request.ExternalId;
        point.Latitude = request.Latitude;
        point.Longitude = request.Longitude;
        point.ApprovalStatus = request.ApprovalStatus;
        point.IsActive = request.ApprovalStatus != PointApprovalStatus.Rejected;

        db.SyncChanges.Add(SyncChangeFactory.ForPoint(point, SyncChangeType.Upsert, now));

        var unblockedCount = 0;
        if (point.ApprovalStatus == PointApprovalStatus.Active)
        {
            var blockedOrders = await db.Orders
                .Where(o => o.TenantId == tenantId && o.PointId == point.Id && o.IsBlockedByPointApproval)
                .ToListAsync(ct);

            foreach (var order in blockedOrders)
            {
                order.IsBlockedByPointApproval = false;
                order.Version++;
                order.UpdatedAt = now;

                db.OrderStatusHistory.Add(new OrderStatusHistory
                {
                    OrderId = order.Id,
                    FromStatus = order.Status,
                    ToStatus = order.Status,
                    ChangedAt = now,
                    IsSystemAction = true,
                    Comment = "Точка подтверждена"
                });

                db.SyncChanges.Add(SyncChangeFactory.ForOrder(order, SyncChangeType.Upsert, now));
                unblockedCount++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new PointUpsertResult(point, unblockedCount);
    }
}
