using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Intake;
using DeliverySystem.Core.Points;
using DeliverySystem.Infrastructure.Intake;
using DeliverySystem.Infrastructure.Points;
using Microsoft.EntityFrameworkCore;

namespace DeliverySystem.Tests;

public sealed class PointScenariosTests
{
    private static readonly DateTimeOffset BeforeCutoff = new(2026, 10, 27, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatePointWithCoordinates_WritesSyncChange_AndAllowsUnknownPointFixture()
    {
        await using var testDb = await TestDatabase.CreateAsync();

        await using (var db = testDb.NewContext())
        {
            var points = new PointService(db);
            var created = await points.UpsertAsync(
                TestDatabase.TenantId,
                new PointUpsertRequest(
                    null,
                    "Новая точка 2ГИС",
                    "ул. Тверская, 12",
                    "+7 900 111-22-33",
                    9999,
                    55.7642,
                    37.6065,
                    PointApprovalStatus.Active));

            Assert.Equal(55.7642, created.Point.Latitude);
            Assert.Equal(37.6065, created.Point.Longitude);
            Assert.Equal(9999, created.Point.ExternalId);
        }

        await using (var db = testDb.NewContext())
        {
            var intake = new MockAmoIntake(
                new OrderIntakeService(db, new WeekendBusinessDayCalculator()),
                new AmoFixtureRepository());

            var result = await intake.RunScenarioAsync("unknown-point", TestDatabase.TenantId, BeforeCutoff);
            Assert.Equal(IntakeStatus.Created, result.Status);
            Assert.NotNull(result.OrderId);

            var pointChangeExists = await db.SyncChanges.AnyAsync(
                c => c.TenantId == TestDatabase.TenantId && c.EntityType == SyncEntityType.Point);
            Assert.True(pointChangeExists);
        }
    }

    [Fact]
    public async Task ApprovePendingPoint_UnblocksOrders_IncrementsVersion_AndAppendsSyncChanges()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        Guid orderId;

        await using (var db = testDb.NewContext())
        {
            var intake = new MockAmoIntake(
                new OrderIntakeService(db, new WeekendBusinessDayCalculator()),
                new AmoFixtureRepository());

            var result = await intake.RunScenarioAsync("pending-point", TestDatabase.TenantId, BeforeCutoff);
            Assert.Equal(IntakeStatus.Created, result.Status);
            orderId = result.OrderId!.Value;

            var blockedOrder = await db.Orders.FirstAsync(o => o.Id == orderId);
            Assert.True(blockedOrder.IsBlockedByPointApproval);
            Assert.Equal(1, blockedOrder.Version);
        }

        await using (var db = testDb.NewContext())
        {
            var points = new PointService(db);
            var upsert = await points.UpsertAsync(
                TestDatabase.TenantId,
                new PointUpsertRequest(
                    TestDatabase.PendingPointId,
                    "Pending point",
                    "ул. Мира, 10",
                    "+7 900 000-00-02",
                    7002,
                    55.7612,
                    37.6095,
                    PointApprovalStatus.Active));

            Assert.Equal(1, upsert.UnblockedOrdersCount);
        }

        await using (var db = testDb.NewContext())
        {
            var order = await db.Orders
                .Include(o => o.StatusHistory)
                .FirstAsync(o => o.Id == orderId);

            Assert.False(order.IsBlockedByPointApproval);
            Assert.Equal(2, order.Version);
            Assert.Contains(order.StatusHistory, h => h.Comment == "Точка подтверждена");
        }
    }

    [Fact]
    public async Task UpsertPoint_DuplicateExternalId_Throws()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        await using var db = testDb.NewContext();
        var points = new PointService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            points.UpsertAsync(
                TestDatabase.TenantId,
                new PointUpsertRequest(
                    null,
                    "Дубликат",
                    "ул. Спорная, 1",
                    null,
                    7001,
                    55.75,
                    37.61,
                    PointApprovalStatus.Active)));
    }
}
