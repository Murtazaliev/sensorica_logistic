using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Intake;
using DeliverySystem.Core.Sync;
using DeliverySystem.Infrastructure.Intake;
using DeliverySystem.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace DeliverySystem.Tests;

public sealed class SyncScenariosTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SameOperationPushedTwice_IsAppliedOnce()
    {
        await using var db = await TestDatabase.CreateAsync();
        var order = await SeedOrderAsync(db, OrderStatus.InTransit, version: 1);
        var operationId = Guid.NewGuid();

        var first = await PushStatusAsync(db, operationId, order.Id, OrderStatus.Delivered, expectedVersion: 1);
        var second = await PushStatusAsync(db, operationId, order.Id, OrderStatus.Delivered, expectedVersion: 1);

        Assert.Equal(SyncOperationStatus.Applied, first.Status);
        Assert.False(first.IsDuplicate);
        Assert.Equal(SyncOperationStatus.Applied, second.Status);
        Assert.True(second.IsDuplicate);

        await using var ctx = db.NewContext();
        var stored = await ctx.Orders
            .AsNoTracking()
            .Include(o => o.StatusHistory)
            .SingleAsync(o => o.Id == order.Id);

        Assert.Equal(OrderStatus.Delivered, stored.Status);
        Assert.Equal(2, stored.Version);
        Assert.Single(stored.StatusHistory);
        Assert.Single(await ctx.SyncChanges.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task StaleVersion_ReturnsConflictAndKeepsServerState()
    {
        await using var db = await TestDatabase.CreateAsync();
        var order = await SeedOrderAsync(db, OrderStatus.InTransit, version: 2);

        var result = await PushStatusAsync(db, Guid.NewGuid(), order.Id, OrderStatus.Delivered, expectedVersion: 1);

        Assert.Equal(SyncOperationStatus.Conflict, result.Status);
        Assert.False(result.IsDuplicate);
        Assert.Contains("version_conflict", result.Error);
        Assert.Contains("\"version\":2", result.ResultJson);

        await using var ctx = db.NewContext();
        var stored = await ctx.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.InTransit, stored.Status);
        Assert.Equal(2, stored.Version);
        Assert.Empty(await ctx.OrderStatusHistory.AsNoTracking().ToListAsync());
        Assert.Empty(await ctx.SyncChanges.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task InvalidTransition_IsRejectedAndRecorded()
    {
        await using var db = await TestDatabase.CreateAsync();
        var order = await SeedOrderAsync(db, OrderStatus.New, version: 1);
        var operationId = Guid.NewGuid();

        var result = await PushStatusAsync(db, operationId, order.Id, OrderStatus.Delivered, expectedVersion: 1);
        var replay = await PushStatusAsync(db, operationId, order.Id, OrderStatus.Delivered, expectedVersion: 1);

        Assert.Equal(SyncOperationStatus.Rejected, result.Status);
        Assert.Contains("invalid_transition", result.Error);
        Assert.True(replay.IsDuplicate);
        Assert.Equal(SyncOperationStatus.Rejected, replay.Status);

        await using var ctx = db.NewContext();
        var stored = await ctx.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.New, stored.Status);
        Assert.Equal(1, stored.Version);
        Assert.Single(await ctx.SyncOperations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Pull_ReturnsDeltaWithTombstonesAfterCursor()
    {
        await using var db = await TestDatabase.CreateAsync();
        var order = await SeedOrderAsync(db, OrderStatus.InTransit, version: 1);

        await PushStatusAsync(db, Guid.NewGuid(), order.Id, OrderStatus.Delivered, expectedVersion: 1);

        await using (var ctx = db.NewContext())
        {
            await new SyncService(ctx).AppendTombstoneAsync(
                TestDatabase.TenantId, SyncEntityType.Point, TestDatabase.PendingPointId, Now);
        }

        await using (var ctx = db.NewContext())
        {
            var service = new SyncService(ctx);

            var page = await service.PullAsync(TestDatabase.TenantId, 0, 100);
            Assert.False(page.HasMore);
            Assert.Equal(2, page.Changes.Count);
            Assert.Equal(SyncEntityType.Order, page.Changes[0].EntityType);
            Assert.Equal(SyncChangeType.Upsert, page.Changes[0].ChangeType);
            Assert.Equal(SyncEntityType.Point, page.Changes[1].EntityType);
            Assert.Equal(SyncChangeType.Tombstone, page.Changes[1].ChangeType);
            Assert.Equal(page.Changes[^1].Cursor, page.NextCursor);

            var empty = await service.PullAsync(TestDatabase.TenantId, page.NextCursor, 100);
            Assert.Empty(empty.Changes);
            Assert.Equal(page.NextCursor, empty.NextCursor);
            Assert.False(empty.HasMore);
        }
    }

    [Fact]
    public async Task Pull_RespectsLimitAndReportsMore()
    {
        await using var db = await TestDatabase.CreateAsync();

        await using (var ctx = db.NewContext())
        {
            var service = new SyncService(ctx);
            await service.AppendTombstoneAsync(TestDatabase.TenantId, SyncEntityType.Point, Guid.NewGuid(), Now);
            await service.AppendTombstoneAsync(TestDatabase.TenantId, SyncEntityType.Problem, Guid.NewGuid(), Now);
        }

        await using (var ctx = db.NewContext())
        {
            var service = new SyncService(ctx);

            var first = await service.PullAsync(TestDatabase.TenantId, 0, 1);
            Assert.Single(first.Changes);
            Assert.True(first.HasMore);

            var second = await service.PullAsync(TestDatabase.TenantId, first.NextCursor, 1);
            Assert.Single(second.Changes);
            Assert.False(second.HasMore);
        }
    }

    [Fact]
    public async Task Pull_DoesNotLeakOtherTenantChanges()
    {
        await using var db = await TestDatabase.CreateAsync();

        await using (var ctx = db.NewContext())
        {
            await new SyncService(ctx).AppendTombstoneAsync(
                TestDatabase.OtherTenantId, SyncEntityType.Point, TestDatabase.PendingPointId, Now);
        }

        await using (var ctx = db.NewContext())
        {
            var service = new SyncService(ctx);

            var own = await service.PullAsync(TestDatabase.TenantId, 0, 100);
            Assert.Empty(own.Changes);

            var other = await service.PullAsync(TestDatabase.OtherTenantId, 0, 100);
            Assert.Single(other.Changes);
        }
    }

    [Fact]
    public async Task Intake_AppendsChangeFeedEntry()
    {
        await using var db = await TestDatabase.CreateAsync();

        await using (var ctx = db.NewContext())
        {
            var intake = new MockAmoIntake(
                new OrderIntakeService(ctx, new WeekendBusinessDayCalculator()),
                new AmoFixtureRepository());

            var result = await intake.RunScenarioAsync(
                "add-today-before-cutoff",
                TestDatabase.TenantId,
                new DateTimeOffset(2026, 10, 27, 6, 30, 0, TimeSpan.Zero));

            Assert.Equal(IntakeStatus.Created, result.Status);
        }

        await using (var ctx = db.NewContext())
        {
            var pull = await new SyncService(ctx).PullAsync(TestDatabase.TenantId, 0, 100);
            var change = Assert.Single(pull.Changes);
            Assert.Equal(SyncEntityType.Order, change.EntityType);
            Assert.Equal(SyncChangeType.Upsert, change.ChangeType);
        }
    }

    private static async Task<Order> SeedOrderAsync(TestDatabase db, OrderStatus status, long version)
    {
        await using var ctx = db.NewContext();

        var order = new Order
        {
            TenantId = TestDatabase.TenantId,
            OrderNumber = "ORD-TEST-000001",
            AmoStageId = 0,
            PointId = TestDatabase.ActivePointId,
            RequestedDate = new DateOnly(2026, 10, 27),
            OriginalRequestedDate = new DateOnly(2026, 10, 27),
            ReceivedAt = Now,
            Status = status,
            Version = version,
            CreatedAt = Now,
            UpdatedAt = Now
        };

        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();
        return order;
    }

    private static async Task<SyncOperationResult> PushStatusAsync(
        TestDatabase db,
        Guid operationId,
        Guid orderId,
        OrderStatus toStatus,
        long expectedVersion)
    {
        await using var ctx = db.NewContext();
        var service = new SyncService(ctx);

        var request = new SyncPushRequest(
            "device-1",
            [
                new SyncPushOperation(
                    operationId,
                    SyncOperationType.SetOrderStatus,
                    SyncJson.Serialize(new SetOrderStatusPayload(orderId, toStatus, expectedVersion)),
                    Now)
            ]);

        var result = await service.PushAsync(TestDatabase.TenantId, request);
        return Assert.Single(result.Results);
    }
}
