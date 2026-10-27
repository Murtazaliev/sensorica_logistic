using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Routes;
using DeliverySystem.Core.Sync;
using DeliverySystem.Infrastructure.Routes;
using Microsoft.EntityFrameworkCore;

namespace DeliverySystem.Tests;

public sealed class RouteScenariosTests
{
    private static readonly DateOnly Date = new(2026, 10, 27);
    private static readonly DateTimeOffset Morning = new(2026, 10, 27, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SetStops_PersistsOrderAndSequence_TotalsAndGeometry()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider(Morning);

        var (first, second, third) = await SeedOrdersAsync(testDb, Date);

        await using (var db = testDb.NewContext())
        {
            var routes = new RouteService(db, clock);
            var draft = await routes.EnsureDraftAsync(TestDatabase.TenantId, TestDatabase.CourierId, Date);
            var details = await routes.SetStopsAsync(
                TestDatabase.TenantId,
                draft.Id,
                [first, second, third]);

            Assert.Equal(3, details.TotalPoints);
            Assert.Equal(3, details.Stops.Count);
            Assert.Equal([1, 2, 3], details.Stops.Select(s => s.SequenceNumber));
            Assert.Equal([first, second, third], details.Stops.Select(s => s.OrderId));
            Assert.Equal(19m, details.TotalWeight);
            Assert.True(details.TotalDistance > 0);
            Assert.Equal(RouteStatus.Draft, details.Status);
        }

        await using (var db = testDb.NewContext())
        {
            var available = await new RouteService(db, clock)
                .ListAvailableOrdersAsync(TestDatabase.TenantId, Date);
            Assert.Empty(available);
        }
    }

    [Fact]
    public async Task AutoOrder_OrdersStopsByNearestNeighbor()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider(Morning);

        var (first, second, third) = await SeedOrdersAsync(testDb, Date);

        await using var db = testDb.NewContext();
        var routes = new RouteService(db, clock);
        var draft = await routes.EnsureDraftAsync(TestDatabase.TenantId, TestDatabase.CourierId, Date);
        await routes.SetStopsAsync(TestDatabase.TenantId, draft.Id, [first, third, second]);

        var details = await routes.AutoOrderAsync(TestDatabase.TenantId, draft.Id);

        Assert.Equal([first, second, third], details.Stops.Select(s => s.OrderId));
        Assert.True(details.Stops.Select(s => s.SequenceNumber).SequenceEqual([1, 2, 3]));
    }

    [Fact]
    public async Task SetStops_RejectsOrderAlreadyInAnotherRoute()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider(Morning);
        var (first, _, _) = await SeedOrdersAsync(testDb, Date);

        await using (var db = testDb.NewContext())
        {
            var otherRoute = new Route
            {
                TenantId = TestDatabase.TenantId,
                CourierId = TestDatabase.CourierId,
                Date = Date.AddDays(1),
                Status = RouteStatus.Draft
            };

            db.Routes.Add(otherRoute);
            db.RoutePoints.Add(new RoutePoint
            {
                RouteId = otherRoute.Id,
                OrderId = first,
                SequenceNumber = 1,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        await using (var db = testDb.NewContext())
        {
            var routes = new RouteService(db, clock);
            var draft = await routes.EnsureDraftAsync(TestDatabase.TenantId, TestDatabase.CourierId, Date);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                routes.SetStopsAsync(TestDatabase.TenantId, draft.Id, [first]));
        }
    }

    [Fact]
    public async Task Planning_IsLockedAfterTenAndConfirmedRouteCannotBeEdited()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider(Morning);
        var (first, _, _) = await SeedOrdersAsync(testDb, Date);

        await using var db = testDb.NewContext();
        var routes = new RouteService(db, clock);
        var draft = await routes.EnsureDraftAsync(TestDatabase.TenantId, TestDatabase.CourierId, Date);
        var withStop = await routes.SetStopsAsync(TestDatabase.TenantId, draft.Id, [first]);
        Assert.Single(withStop.Stops);

        clock.UtcNow = new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
        Assert.True(await routes.IsPlanningLockedAsync(TestDatabase.TenantId, Date));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            routes.SetStopsAsync(TestDatabase.TenantId, draft.Id, []));

        clock.UtcNow = Morning;
        var confirmed = await routes.ConfirmAsync(TestDatabase.TenantId, draft.Id);
        Assert.Equal(RouteStatus.Confirmed, confirmed.Status);
        Assert.True(confirmed.IsLocked);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            routes.SetStopsAsync(TestDatabase.TenantId, draft.Id, []));
    }

    [Fact]
    public async Task GetCourierIdForUser_ReturnsCourierProfile()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider(Morning);

        await using var db = testDb.NewContext();
        var routes = new RouteService(db, clock);

        var courierId = await routes.GetCourierIdForUserAsync(
            TestDatabase.TenantId,
            TestDatabase.CourierUserId);

        Assert.Equal(TestDatabase.CourierId, courierId);
    }

    [Fact]
    public async Task SetStopsAndConfirm_AppendRouteChanges()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider(Morning);
        var (first, _, _) = await SeedOrdersAsync(testDb, Date);

        await using var db = testDb.NewContext();
        var routes = new RouteService(db, clock);
        var draft = await routes.EnsureDraftAsync(TestDatabase.TenantId, TestDatabase.CourierId, Date);
        await routes.SetStopsAsync(TestDatabase.TenantId, draft.Id, [first]);
        await routes.ConfirmAsync(TestDatabase.TenantId, draft.Id);

        var changes = await db.SyncChanges
            .AsNoTracking()
            .Where(c => c.EntityType == SyncEntityType.Route)
            .OrderBy(c => c.Id)
            .ToListAsync();

        Assert.Equal(2, changes.Count);
        var snapshot = SyncJson.Deserialize<RouteSyncSnapshot>(changes[^1].PayloadJson);
        Assert.NotNull(snapshot);
        Assert.Equal(RouteStatus.Confirmed, snapshot!.Status);
        Assert.Single(snapshot.Stops);
    }

    [Fact]
    public async Task AutoOrder_UsesRoadDistanceMatrix()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider(Morning);
        var (first, second, third) = await SeedOrdersAsync(testDb, Date);

        await using var db = testDb.NewContext();
        var routes = new RouteService(
            db,
            clock,
            null,
            new FakeRoadRouter(),
            new FakeRoadMatrix());

        var draft = await routes.EnsureDraftAsync(TestDatabase.TenantId, TestDatabase.CourierId, Date);
        await routes.SetStopsAsync(TestDatabase.TenantId, draft.Id, [first, second, third]);

        var details = await routes.AutoOrderAsync(TestDatabase.TenantId, draft.Id);

        Assert.Equal([first, third, second], details.Stops.Select(s => s.OrderId));
    }

    [Fact]
    public async Task SetStops_UsesRoadRouterDistanceAndGeometry()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider(Morning);
        var (first, second, third) = await SeedOrdersAsync(testDb, Date);

        await using var db = testDb.NewContext();
        var routes = new RouteService(db, clock, null, new FakeRoadRouter());

        var draft = await routes.EnsureDraftAsync(TestDatabase.TenantId, TestDatabase.CourierId, Date);
        var details = await routes.SetStopsAsync(
            TestDatabase.TenantId,
            draft.Id,
            [first, second, third]);

        Assert.Equal(12.0m, details.TotalDistance);
        Assert.Equal(30, details.TotalDuration);
        Assert.Equal(3, details.RoadGeometry.Count);
        Assert.Equal(55.75, details.RoadGeometry[0].Latitude);
    }

    [Fact]
    public async Task Confirm_EmptyRoute_Throws()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider(Morning);

        await using var db = testDb.NewContext();
        var routes = new RouteService(db, clock);
        var draft = await routes.EnsureDraftAsync(TestDatabase.TenantId, TestDatabase.CourierId, Date);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            routes.ConfirmAsync(TestDatabase.TenantId, draft.Id));
    }

    private static async Task<(Guid First, Guid Second, Guid Third)> SeedOrdersAsync(
        TestDatabase testDb,
        DateOnly date)
    {
        await using var db = testDb.NewContext();

        var first = await AddOrderAsync(db, date, "ORD-A", 55.7500, 37.6000, 5);
        var second = await AddOrderAsync(db, date, "ORD-B", 55.7600, 37.6100, 8);
        var third = await AddOrderAsync(db, date, "ORD-C", 55.8000, 37.7000, 6);

        return (first, second, third);
    }

    private static async Task<Guid> AddOrderAsync(
        DeliverySystem.Infrastructure.Persistence.AppDbContext db,
        DateOnly date,
        string number,
        double latitude,
        double longitude,
        decimal weight)
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
            RequestedDate = date,
            OriginalRequestedDate = date,
            Status = OrderStatus.New,
            PointId = point.Id,
            Point = point,
            TotalWeight = weight,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return order.Id;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FakeRoadRouter : IRoadRouter
    {
        public Task<RoadRoute?> BuildAsync(
            IReadOnlyList<GeoCoordinate> points,
            CancellationToken ct = default)
        {
            IReadOnlyList<GeoCoordinate> geometry =
            [
                new GeoCoordinate(55.75, 37.60),
                new GeoCoordinate(55.76, 37.61),
                new GeoCoordinate(55.80, 37.70)
            ];

            return Task.FromResult<RoadRoute?>(new RoadRoute(12_000, 1_800, geometry));
        }
    }

    private sealed class FakeRoadMatrix : IRoadDistanceMatrix
    {
        public Task<RoadDistanceMatrix?> BuildAsync(
            IReadOnlyList<GeoCoordinate> points,
            CancellationToken ct = default)
        {
            var distances = new[,]
            {
                { 0, 100, 1 },
                { 100, 0, 1 },
                { 1, 1, 0 }
            };

            var durations = new int[3, 3];
            return Task.FromResult<RoadDistanceMatrix?>(
                new RoadDistanceMatrix(distances, durations));
        }
    }
}
