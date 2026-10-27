using System.Security.Cryptography;
using System.Text;
using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Intake;
using DeliverySystem.Core.Sync;
using DeliverySystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliverySystem.Infrastructure.Intake;

public sealed class OrderIntakeService(AppDbContext db, IBusinessDayCalculator calendar)
{
    public async Task<IntakeResult> HandleEventAsync(LeadEvent evt, CancellationToken ct = default)
    {
        var eventId = ComputeEventId(evt);

        var alreadyProcessed = await db.WebhookLogs.AnyAsync(
            x => x.TenantId == evt.TenantId && x.ExternalEventId == eventId && x.ProcessedAt != null, ct);

        if (alreadyProcessed)
        {
            return new IntakeResult(IntakeStatus.Duplicate, null, "Event already processed");
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == evt.TenantId, ct);
        if (tenant is null)
        {
            return new IntakeResult(IntakeStatus.ValidationError, null, "Tenant not found");
        }

        var log = NewLog(evt, eventId);

        if (IsCancellation(evt))
        {
            return await HandleCancellationAsync(evt, log, ct);
        }

        var existing = await db.Orders.FirstOrDefaultAsync(
            x => x.TenantId == evt.TenantId && x.AmoLeadId == evt.AmoLeadId, ct);

        if (existing is not null)
        {
            var updatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedAt = updatedAt;
            log.ProcessedAt = updatedAt;
            log.WasDeferred = existing.IsDeferred;
            db.WebhookLogs.Add(log);
            db.SyncChanges.Add(SyncChangeFactory.ForOrder(existing, SyncChangeType.Upsert, updatedAt));
            await db.SaveChangesAsync(ct);
            return new IntakeResult(IntakeStatus.Updated, existing.Id, "Order already exists");
        }

        Point? point = null;
        if (evt.PointExternalId is not null)
        {
            point = await db.Points.FirstOrDefaultAsync(
                x => x.TenantId == evt.TenantId && x.ExternalId == evt.PointExternalId, ct);
        }

        if (point is null)
        {
            log.ProcessingError = $"Unknown point (externalId={evt.PointExternalId?.ToString() ?? "null"})";
            db.WebhookLogs.Add(log);
            await db.SaveChangesAsync(ct);
            return new IntakeResult(IntakeStatus.ValidationError, null, log.ProcessingError);
        }

        var (today, localTime) = GetTenantNow(tenant, evt.OccurredAt);
        var requested = evt.RequestedDate ?? today;
        var effectiveDate = requested;
        var isDeferred = false;
        OrderDeferredReason? deferredReason = null;
        OrderValidationError? validationError = null;

        if (requested < today)
        {
            validationError = OrderValidationError.InvalidDate;
        }
        else if (requested == today && localTime >= tenant.CutoffTimeForTodayOrders)
        {
            effectiveDate = calendar.NextBusinessDay(today);
            isDeferred = true;
            deferredReason = OrderDeferredReason.AfterCutoff;
        }

        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            TenantId = evt.TenantId,
            OrderNumber = await NextOrderNumberAsync(evt.TenantId, today, ct),
            AmoLeadId = evt.AmoLeadId,
            AmoStageId = evt.StageId,
            PointId = point.Id,
            SalesManagerUserId = null,
            RequestedDate = effectiveDate,
            OriginalRequestedDate = requested,
            IsDeferred = isDeferred,
            DeferredReason = deferredReason,
            ValidationError = validationError,
            IsBlockedByPointApproval = point.ApprovalStatus != PointApprovalStatus.Active,
            ReceivedAt = evt.OccurredAt,
            Status = OrderStatus.New,
            TotalWeight = evt.Items.Sum(i => i.Weight),
            TotalVolume = 0m,
            TotalAmount = 0m,
            Currency = "RUB",
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var item in evt.Items)
        {
            order.Items.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                Unit = item.Unit,
                Weight = item.Weight
            });
        }

        order.StatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = null,
            ToStatus = OrderStatus.New,
            ChangedAt = now,
            IsSystemAction = true,
            Comment = "Создан из amoCRM",
            CorrelationId = eventId
        });

        log.ProcessedAt = now;
        log.WasDeferred = isDeferred;

        db.WebhookLogs.Add(log);
        db.Orders.Add(order);
        db.SyncChanges.Add(SyncChangeFactory.ForOrder(order, SyncChangeType.Upsert, now));
        await db.SaveChangesAsync(ct);

        if (validationError is not null)
        {
            return new IntakeResult(IntakeStatus.ValidationError, order.Id, $"Validation error: {validationError}");
        }

        if (isDeferred)
        {
            return new IntakeResult(IntakeStatus.Deferred, order.Id,
                $"Deferred to {effectiveDate:yyyy-MM-dd} (cutoff {tenant.CutoffTimeForTodayOrders:HH\\:mm})");
        }

        return new IntakeResult(IntakeStatus.Created, order.Id, null);
    }

    private async Task<IntakeResult> HandleCancellationAsync(LeadEvent evt, WebhookLog log, CancellationToken ct)
    {
        var order = await db.Orders
            .FirstOrDefaultAsync(o => o.TenantId == evt.TenantId && o.AmoLeadId == evt.AmoLeadId, ct);

        if (order is null)
        {
            log.ProcessingError = "Order not found for cancellation";
            db.WebhookLogs.Add(log);
            await db.SaveChangesAsync(ct);
            return new IntakeResult(IntakeStatus.ValidationError, null, log.ProcessingError);
        }

        var now = DateTimeOffset.UtcNow;
        var fromStatus = order.Status;

        order.Status = OrderStatus.Cancelled;
        order.CancelledReason = string.IsNullOrWhiteSpace(evt.StageName) ? "Отменён из amoCRM" : evt.StageName;
        order.UpdatedAt = now;
        order.Version++;

        db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = fromStatus,
            ToStatus = OrderStatus.Cancelled,
            ChangedAt = now,
            IsSystemAction = true,
            Comment = "Отменён из amoCRM",
            CorrelationId = log.ExternalEventId
        });

        log.ProcessedAt = now;
        log.WasDeferred = order.IsDeferred;

        db.WebhookLogs.Add(log);
        db.SyncChanges.Add(SyncChangeFactory.ForOrder(order, SyncChangeType.Upsert, now));
        await db.SaveChangesAsync(ct);

        return new IntakeResult(IntakeStatus.Cancelled, order.Id, "Order cancelled");
    }

    private async Task<string> NextOrderNumberAsync(Guid tenantId, DateOnly today, CancellationToken ct)
    {
        var count = await db.Orders.CountAsync(x => x.TenantId == tenantId, ct);
        return $"ORD-{today.Year}-{count + 1:D6}";
    }

    private static (DateOnly Today, TimeOnly Time) GetTenantNow(Tenant tenant, DateTimeOffset occurredAt)
    {
        var tz = TimeZoneResolver.Resolve(tenant.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(occurredAt, tz);
        return (DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
    }

    private static bool IsCancellation(LeadEvent evt)
    {
        if (evt.EventType.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return evt.StageName is not null &&
               (evt.StageName.Contains("не реализовано", StringComparison.OrdinalIgnoreCase) ||
                evt.StageName.Contains("отмен", StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeEventId(LeadEvent evt)
    {
        var raw = string.Join('|',
            evt.EventType,
            evt.AmoLeadId,
            evt.StageId,
            evt.RequestedDate?.ToString("yyyy-MM-dd") ?? "-",
            evt.PointExternalId?.ToString() ?? "-",
            evt.ResponsibleAmoUserId?.ToString() ?? "-");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static WebhookLog NewLog(LeadEvent evt, string eventId) => new()
    {
        TenantId = evt.TenantId,
        Source = WebhookSource.AmoCRM,
        EventType = evt.EventType,
        ExternalEventId = eventId,
        PayloadJson = evt.RawJson,
        SignatureValid = true,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
