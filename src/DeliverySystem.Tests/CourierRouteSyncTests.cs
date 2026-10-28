using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Routes;
using DeliverySystem.Core.Sync;
using DeliverySystem.Infrastructure.Persistence;
using DeliverySystem.Infrastructure.Routes;
using DeliverySystem.Infrastructure.Sync;

namespace DeliverySystem.Tests;

public sealed class CourierRouteSyncTests
{
    private static readonly DateOnly Date = new(2026, 10, 27);
    private static readonly DateTimeOffset Morning = new(2026, 10, 27, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AcceptRoute_IsIdempotent()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var clock = new FixedTimeProvider(Morning);

        await using var db = testDb.NewContext();
        var routes = new RouteService(db, clock);
        var sync = new SyncService(db, routes);
        var orderId = await SeedOrderAsync(db, "ORD-ROUTE-1");
        var draft = await routes.EnsureDraftAsync(TestDatabase.TenantId, TestDatabase.CourierId, Date);
        await routes.SetStopsAsync(TestDatabase.TenantId, draft.Id, [orderId]);
        await routes.ConfirmAsync(TestDatabase.TenantId, draft.Id);

        var operationId = Guid.NewGuid();
        var request = new SyncPushRequest("courier-test",
        [
            new SyncPushOperation(
                operationId,
                SyncOperationType.AcceptRoute,
                SyncJson.Serialize(new RouteOperationPayload(draft.Id)),
                DateTimeOffset.UtcNow)
        ]);

        var first = await sync.PushCourierAsync(TestDatabase.TenantId, TestDatabase.CourierId, request);
        Assert.Equal(SyncOperationStatus.Applied, first.Results[0].Status);
        Assert.False(first.Results[0].IsDuplicate);

        var duplicate = await sync.PushCourierAsync(TestDatabase.TenantId, TestDatabase.CourierId, request);
        Assert.Equal(SyncOperationStatus.Applied, duplicate.Results[0].Status);
        Assert.True(duplicate.Results[0].IsDuplicate);

        var route = await routes.GetAsync(TestDatabase.TenantId, draft.Id);
        Assert.NotNull(route);
        Assert.NotNull(route!.AcceptedAt);
    }

    [Fact]
    public async Task RouteLifecycleOperations_CompleteRoute()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var clock = new FixedTimeProvider(Morning);

        await using var db = testDb.NewContext();
        var routes = new RouteService(db, clock);
        var sync = new SyncService(db, routes);
        var firstOrder = await SeedOrderAsync(db, "ORD-ROUTE-2", 55.75, 37.60);
        var secondOrder = await SeedOrderAsync(db, "ORD-ROUTE-3", 55.76, 37.61);
        var draft = await routes.EnsureDraftAsync(TestDatabase.TenantId, TestDatabase.CourierId, Date);
        var confirmed = await routes.SetStopsAsync(
            TestDatabase.TenantId, draft.Id, [firstOrder, secondOrder]);
        await routes.ConfirmAsync(TestDatabase.TenantId, draft.Id);

        await PushAsync(sync, new RouteOperationPayload(draft.Id), SyncOperationType.AcceptRoute);
        await PushAsync(sync, new RouteOperationPayload(draft.Id), SyncOperationType.StartRoute);

        var firstStop = confirmed.Stops.First(s => s.OrderId == firstOrder);
        var secondStop = confirmed.Stops.First(s => s.OrderId == secondOrder);

        await PushAsync(
            sync,
            new RoutePointOperationPayload(draft.Id, firstStop.Id),
            SyncOperationType.ArriveRoutePoint);
        await PushAsync(
            sync,
            new RoutePointOperationPayload(draft.Id, firstStop.Id),
            SyncOperationType.DeliverRoutePoint);
        await PushAsync(
            sync,
            new RoutePointOperationPayload(draft.Id, secondStop.Id),
            SyncOperationType.ArriveRoutePoint);
        await PushAsync(
            sync,
            new RoutePointProblemPayload(draft.Id, secondStop.Id, "Закрыто"),
            SyncOperationType.ProblemRoutePoint);

        var completed = await routes.GetAsync(TestDatabase.TenantId, draft.Id);
        Assert.NotNull(completed);
        Assert.Equal(RouteStatus.Completed, completed!.Status);
        Assert.Equal(OrderStatus.Delivered, completed.Stops.First(s => s.OrderId == firstOrder).OrderStatus);
        Assert.Equal(OrderStatus.Problem, completed.Stops.First(s => s.OrderId == secondOrder).OrderStatus);
    }

    private static async Task PushAsync(
        SyncService sync,
        object payload,
        SyncOperationType type)
    {
        var request = new SyncPushRequest("courier-test",
        [
            new SyncPushOperation(
                Guid.NewGuid(),
                type,
                SyncJson.Serialize(payload),
                DateTimeOffset.UtcNow)
        ]);

        var result = await sync.PushCourierAsync(
            TestDatabase.TenantId,
            TestDatabase.CourierId,
            request);

        Assert.Equal(SyncOperationStatus.Applied, result.Results[0].Status);
    }

    private static async Task<Guid> SeedOrderAsync(
        AppDbContext db,
        string number,
        double latitude = 55.75,
        double longitude = 37.60)
    {
        var point = new Point
        {
            TenantId = TestDatabase.TenantId,
            Name = "Point " + number,
            Address = "Address " + number,
            Latitude = latitude,
            Longitude = longitude,
            ApprovalStatus = PointApprovalStatus.Active
        };

        var order = new Order
        {
            TenantId = TestDatabase.TenantId,
            OrderNumber = number,
            RequestedDate = Date,
            OriginalRequestedDate = Date,
            Status = OrderStatus.New,
            PointId = point.Id,
            Point = point,
            TotalWeight = 5,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
