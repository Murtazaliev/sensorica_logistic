using DeliverySystem.Core.Domain;
using DeliverySystem.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DeliverySystem.Tests;

internal sealed class TestDatabase : IAsyncDisposable
{
    public static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherTenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    public static readonly Guid ActivePointId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid PendingPointId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid CourierUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid CourierId = Guid.Parse("44444444-4444-4444-4444-444444444401");

    private readonly string _connectionString;
    private readonly SqliteConnection _keeper;

    private TestDatabase(string connectionString, SqliteConnection keeper)
    {
        _connectionString = connectionString;
        _keeper = keeper;
    }

    public static async Task<TestDatabase> CreateAsync()
    {
        var connectionString = $"Data Source=file:ds-{Guid.NewGuid():N}?mode=memory&cache=shared";
        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();

        var database = new TestDatabase(connectionString, keeper);

        await using var db = database.NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Tenants.AddRange(
            new Tenant
            {
                Id = TenantId,
                Name = "Test tenant",
                TimeZoneId = "Europe/Moscow",
                DeliveryStartTime = new TimeOnly(10, 0),
                CutoffTimeForTodayOrders = new TimeOnly(10, 0),
                PlanningLockTime = new TimeOnly(10, 0)
            },
            new Tenant
            {
                Id = OtherTenantId,
                Name = "Other tenant",
                TimeZoneId = "Europe/Moscow"
            });

        var courierUser = new AppUser
        {
            Id = CourierUserId,
            TenantId = TenantId,
            Email = "courier@test.local",
            FullName = "Test Courier",
            PasswordHash = "not-used",
            Role = UserRole.Courier
        };

        db.Users.Add(courierUser);

        db.Couriers.Add(new Courier
        {
            Id = CourierId,
            TenantId = TenantId,
            UserId = CourierUserId,
            User = courierUser,
            VehicleType = VehicleType.Car,
            CapacityWeight = 500,
            IsAvailable = true
        });

        db.Points.AddRange(
            new Point
            {
                Id = ActivePointId,
                TenantId = TenantId,
                Name = "Active point",
                Address = "ул. Ленина, 5",
                ExternalId = 7001,
                ApprovalStatus = PointApprovalStatus.Active
            },
            new Point
            {
                Id = PendingPointId,
                TenantId = TenantId,
                Name = "Pending point",
                Address = "ул. Мира, 10",
                ExternalId = 7002,
                ApprovalStatus = PointApprovalStatus.PendingApproval
            });

        await db.SaveChangesAsync();
        return database;
    }

    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        return new AppDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await _keeper.DisposeAsync();
    }
}
