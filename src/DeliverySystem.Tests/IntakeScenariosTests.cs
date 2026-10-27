using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Intake;
using DeliverySystem.Infrastructure.Intake;
using DeliverySystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliverySystem.Tests;

public sealed class IntakeScenariosTests
{
    private static readonly DateTimeOffset BeforeCutoff = new(2026, 10, 27, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterCutoff = new(2026, 10, 27, 7, 20, 0, TimeSpan.Zero);

    [Fact]
    public async Task BeforeCutoff_TodayOrder_IsCreatedForToday()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.RunAsync("add-today-before-cutoff", BeforeCutoff);

        Assert.Equal(IntakeStatus.Created, result.Status);
        var order = Assert.Single(await h.OrdersAsync());
        Assert.Equal(new DateOnly(2026, 10, 27), order.RequestedDate);
        Assert.Equal(new DateOnly(2026, 10, 27), order.OriginalRequestedDate);
        Assert.False(order.IsDeferred);
        Assert.Null(order.DeferredReason);
        Assert.Equal(OrderStatus.New, order.Status);
        Assert.False(order.IsBlockedByPointApproval);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(15m, order.TotalWeight);
        Assert.Single(await h.WebhookLogsAsync());
    }

    [Fact]
    public async Task AfterCutoff_TodayOrder_IsDeferredToNextBusinessDay()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.RunAsync("add-today-after-cutoff", AfterCutoff);

        Assert.Equal(IntakeStatus.Deferred, result.Status);
        var order = Assert.Single(await h.OrdersAsync());
        Assert.True(order.IsDeferred);
        Assert.Equal(OrderDeferredReason.AfterCutoff, order.DeferredReason);
        Assert.Equal(new DateOnly(2026, 10, 27), order.OriginalRequestedDate);
        Assert.Equal(new DateOnly(2026, 10, 28), order.RequestedDate);
        var log = Assert.Single(await h.WebhookLogsAsync());
        Assert.True(log.WasDeferred);
    }

    [Fact]
    public async Task TomorrowOrder_IsAcceptedWithoutDeferral()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.RunAsync("add-tomorrow", BeforeCutoff);

        Assert.Equal(IntakeStatus.Created, result.Status);
        var order = Assert.Single(await h.OrdersAsync());
        Assert.False(order.IsDeferred);
        Assert.Equal(new DateOnly(2026, 10, 28), order.RequestedDate);
    }

    [Fact]
    public async Task PastDate_IsValidationError()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.RunAsync("add-past-date", BeforeCutoff);

        Assert.Equal(IntakeStatus.ValidationError, result.Status);
        var order = Assert.Single(await h.OrdersAsync());
        Assert.Equal(OrderValidationError.InvalidDate, order.ValidationError);
        Assert.Equal(new DateOnly(2026, 10, 26), order.RequestedDate);
        Assert.False(order.IsDeferred);
    }

    [Fact]
    public async Task DuplicateWebhook_DoesNotCreateSecondOrder()
    {
        await using var h = await Harness.CreateAsync();

        var first = await h.RunAsync("add-today-before-cutoff", BeforeCutoff);
        var second = await h.RunAsync("duplicate-webhook", BeforeCutoff);

        Assert.Equal(IntakeStatus.Created, first.Status);
        Assert.Equal(IntakeStatus.Duplicate, second.Status);
        Assert.Single(await h.OrdersAsync());
        Assert.Single(await h.WebhookLogsAsync());
    }

    [Fact]
    public async Task Cancellation_TransitionsOrderToCancelled()
    {
        await using var h = await Harness.CreateAsync();

        await h.RunAsync("add-today-before-cutoff", BeforeCutoff);
        var result = await h.RunAsync("cancel-lead", BeforeCutoff);

        Assert.Equal(IntakeStatus.Cancelled, result.Status);
        var order = Assert.Single(await h.OrdersAsync());
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(2, order.StatusHistory.Count);
        Assert.Contains(order.StatusHistory, x => x.ToStatus == OrderStatus.Cancelled);
    }

    [Fact]
    public async Task PendingPoint_BlocksOrderFromPlanning()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.RunAsync("pending-point", BeforeCutoff);

        Assert.Equal(IntakeStatus.Created, result.Status);
        var order = Assert.Single(await h.OrdersAsync());
        Assert.True(order.IsBlockedByPointApproval);
    }

    [Fact]
    public async Task UnknownPoint_ReturnsValidationErrorAndCreatesNoOrder()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.RunAsync("unknown-point", BeforeCutoff);

        Assert.Equal(IntakeStatus.ValidationError, result.Status);
        Assert.Empty(await h.OrdersAsync());
        var log = Assert.Single(await h.WebhookLogsAsync());
        Assert.NotNull(log.ProcessingError);
        Assert.Null(log.ProcessedAt);
    }

    [Fact]
    public async Task MissingResponsible_CreatesOrderWithoutSalesManager()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.RunAsync("add-missing-responsible", BeforeCutoff);

        Assert.Equal(IntakeStatus.Created, result.Status);
        var order = Assert.Single(await h.OrdersAsync());
        Assert.Null(order.SalesManagerUserId);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly TestDatabase _database;

        private Harness(TestDatabase database) => _database = database;

        public static async Task<Harness> CreateAsync() => new(await TestDatabase.CreateAsync());

        public AppDbContext NewContext() => _database.NewContext();

        public async Task<IntakeResult> RunAsync(string scenario, DateTimeOffset now)
        {
            await using var db = NewContext();
            var service = new OrderIntakeService(db, new WeekendBusinessDayCalculator());
            var intake = new MockAmoIntake(service, new AmoFixtureRepository());
            return await intake.RunScenarioAsync(scenario, TestDatabase.TenantId, now);
        }

        public async Task<List<Order>> OrdersAsync()
        {
            await using var db = NewContext();
            return await db.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .Include(o => o.StatusHistory)
                .ToListAsync();
        }

        public async Task<List<WebhookLog>> WebhookLogsAsync()
        {
            await using var db = NewContext();
            return await db.WebhookLogs.AsNoTracking().ToListAsync();
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }
}
