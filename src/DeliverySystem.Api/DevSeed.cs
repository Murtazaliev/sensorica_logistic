using DeliverySystem.Core.Domain;
using DeliverySystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public static class DevSeed
{
    public static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid ActivePointId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid PendingPointId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Courier1Id = Guid.Parse("44444444-4444-4444-4444-444444444401");
    public static readonly Guid Courier2Id = Guid.Parse("44444444-4444-4444-4444-444444444402");
    public static readonly Guid Courier3Id = Guid.Parse("44444444-4444-4444-4444-444444444403");

    private static readonly (string Email, string FullName, UserRole Role)[] DemoUsers =
    [
        ("admin@demo.local", "Администратор", UserRole.Admin),
        ("dispatcher@demo.local", "Диспетчер", UserRole.Dispatcher),
        ("courier@demo.local", "Курьер", UserRole.Courier),
        ("courier2@demo.local", "Курьер 2", UserRole.Courier),
        ("courier3@demo.local", "Курьер 3", UserRole.Courier),
        ("point@demo.local", "Менеджер точки", UserRole.PointManager),
        ("sales@demo.local", "Менеджер продаж", UserRole.SalesManager),
        ("head@demo.local", "Руководитель", UserRole.Head)
    ];

    public static void EnsureSeeded(AppDbContext db, IPasswordHasher<AppUser> hasher)
    {
        EnsurePointCoordinateColumns(db);
        EnsureUsersTable(db);
        EnsureRouteTables(db);

        if (!db.Tenants.Any())
        {
            db.Tenants.Add(new Tenant
            {
                Id = TenantId,
                Name = "Демо-цех",
                TimeZoneId = "Europe/Moscow",
                DeliveryStartTime = new TimeOnly(10, 0),
                CutoffTimeForTodayOrders = new TimeOnly(10, 0),
                PlanningLockTime = new TimeOnly(10, 0)
            });

            db.Points.AddRange(
                new Point
                {
                    Id = ActivePointId,
                    TenantId = TenantId,
                    Name = "Кафе Уют",
                    Address = "ул. Ленина, 5",
                    Phone = "+7 900 000-00-01",
                    ExternalId = 7001,
                    Latitude = 55.7558,
                    Longitude = 37.6173,
                    ApprovalStatus = PointApprovalStatus.Active
                },
                new Point
                {
                    Id = PendingPointId,
                    TenantId = TenantId,
                    Name = "Магазин Пятёрочка",
                    Address = "ул. Мира, 10",
                    Phone = "+7 900 000-00-02",
                    ExternalId = 7002,
                    Latitude = 55.7612,
                    Longitude = 37.6095,
                    ApprovalStatus = PointApprovalStatus.PendingApproval
                });
        }
        else
        {
            var active = db.Points.FirstOrDefault(p => p.Id == ActivePointId);
            if (active is not null && active.Latitude is null)
            {
                active.Latitude = 55.7558;
                active.Longitude = 37.6173;
            }

            var pending = db.Points.FirstOrDefault(p => p.Id == PendingPointId);
            if (pending is not null && pending.Latitude is null)
            {
                pending.Latitude = 55.7612;
                pending.Longitude = 37.6095;
            }
        }

        EnsureDemoPoints(db);
        EnsureDemoUsers(db, hasher);
        db.SaveChanges();

        EnsureDemoCouriers(db);
        db.SaveChanges();
    }

    private static void EnsureDemoPoints(AppDbContext db)
    {
        AddPointIfMissing(db, 7003, "Пекарня Север", "ул. Тверская, 12", "+7 900 000-00-03", 55.7642, 37.6065);
        AddPointIfMissing(db, 7004, "Кофейня Восток", "ул. Арбат, 20", "+7 900 000-00-04", 55.7515, 37.5930);
    }

    private static void AddPointIfMissing(
        AppDbContext db,
        long externalId,
        string name,
        string address,
        string phone,
        double latitude,
        double longitude)
    {
        if (db.Points.Any(p => p.TenantId == TenantId && p.ExternalId == externalId))
        {
            return;
        }

        db.Points.Add(new Point
        {
            TenantId = TenantId,
            Name = name,
            Address = address,
            Phone = phone,
            ExternalId = externalId,
            Latitude = latitude,
            Longitude = longitude,
            ApprovalStatus = PointApprovalStatus.Active
        });
    }

    private static void EnsureDemoCouriers(AppDbContext db)
    {
        var seeds = new[]
        {
            ("courier@demo.local", Courier1Id, VehicleType.Car, "А123ВС77", 500m),
            ("courier2@demo.local", Courier2Id, VehicleType.Motorcycle, "М456ОР77", 120m),
            ("courier3@demo.local", Courier3Id, VehicleType.OnFoot, (string?)null, 60m)
        };

        foreach (var seed in seeds)
        {
            var user = db.Users.FirstOrDefault(u => u.Email == seed.Item1);
            if (user is null || db.Couriers.Any(c => c.UserId == user.Id))
            {
                continue;
            }

            db.Couriers.Add(new Courier
            {
                Id = seed.Item2,
                TenantId = TenantId,
                UserId = user.Id,
                Type = CourierType.Staff,
                VehicleType = seed.Item3,
                VehiclePlate = seed.Item4,
                CapacityWeight = seed.Item5,
                CapacityVolume = 1m,
                IsAvailable = true
            });
        }
    }

    private static void EnsureDemoUsers(AppDbContext db, IPasswordHasher<AppUser> hasher)
    {
        foreach (var seed in DemoUsers)
        {
            var email = seed.Email.ToLowerInvariant();
            if (db.Users.Any(u => u.Email == email))
            {
                continue;
            }

            var user = new AppUser
            {
                TenantId = TenantId,
                Email = email,
                FullName = seed.FullName,
                Role = seed.Role,
                PreferredLanguage = "ru"
            };

            user.PasswordHash = hasher.HashPassword(user, "demo123");
            db.Users.Add(user);
        }
    }

    private static void EnsurePointCoordinateColumns(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            conn.Open();
        }

        try
        {
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info('Points');";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    existingColumns.Add(reader.GetString(1));
                }
            }

            if (!existingColumns.Contains("Latitude"))
            {
                using var alterLat = conn.CreateCommand();
                alterLat.CommandText = "ALTER TABLE \"Points\" ADD COLUMN \"Latitude\" REAL NULL;";
                alterLat.ExecuteNonQuery();
            }

            if (!existingColumns.Contains("Longitude"))
            {
                using var alterLng = conn.CreateCommand();
                alterLng.CommandText = "ALTER TABLE \"Points\" ADD COLUMN \"Longitude\" REAL NULL;";
                alterLng.ExecuteNonQuery();
            }
        }
        finally
        {
            if (shouldClose)
            {
                conn.Close();
            }
        }
    }

    private static void EnsureUsersTable(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Users" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
                "TenantId" TEXT NOT NULL,
                "Email" TEXT NOT NULL,
                "Phone" TEXT NULL,
                "FullName" TEXT NOT NULL,
                "PasswordHash" TEXT NOT NULL,
                "Role" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "LastLoginAt" TEXT NULL,
                "PreferredLanguage" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL
            );
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_Users_TenantId_Role" ON "Users" ("TenantId", "Role");
            """);
    }

    private static void EnsureRouteTables(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Couriers" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Couriers" PRIMARY KEY,
                "TenantId" TEXT NOT NULL,
                "UserId" TEXT NOT NULL,
                "Type" TEXT NOT NULL,
                "VehicleType" TEXT NOT NULL,
                "VehiclePlate" TEXT NULL,
                "CapacityWeight" TEXT NOT NULL,
                "CapacityVolume" TEXT NOT NULL,
                "MaxPointsPerDay" INTEGER NULL,
                "Rating" REAL NOT NULL,
                "IsAvailable" INTEGER NOT NULL,
                "BlockedReason" TEXT NULL
            );
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Couriers_UserId" ON "Couriers" ("UserId");
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Routes" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Routes" PRIMARY KEY,
                "TenantId" TEXT NOT NULL,
                "CourierId" TEXT NOT NULL,
                "Date" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "IsLocked" INTEGER NOT NULL,
                "LockedAt" TEXT NULL,
                "PlannedStartTime" TEXT NOT NULL,
                "TotalPoints" INTEGER NOT NULL,
                "TotalWeight" TEXT NOT NULL,
                "TotalVolume" TEXT NOT NULL,
                "TotalDistance" TEXT NOT NULL,
                "TotalDuration" INTEGER NOT NULL,
                "Geometry" TEXT NULL,
                "BuiltByAlgorithm" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                "ConfirmedAt" TEXT NULL,
                "AcceptedAt" TEXT NULL,
                "CancelledReason" TEXT NULL
            );
            """);

        EnsureRouteAcceptedAtColumn(db);

        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Routes_CourierId_Date" ON "Routes" ("CourierId", "Date");
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_Routes_TenantId_Date" ON "Routes" ("TenantId", "Date");
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "RoutePoints" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_RoutePoints" PRIMARY KEY,
                "RouteId" TEXT NOT NULL,
                "OrderId" TEXT NOT NULL,
                "SequenceNumber" INTEGER NOT NULL,
                "PlannedArrivalFrom" TEXT NULL,
                "PlannedArrivalTo" TEXT NULL,
                "ActualArrivalAt" TEXT NULL,
                "CompletedAt" TEXT NULL,
                "Status" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "ReassignedToId" TEXT NULL
            );
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_RoutePoints_RouteId_SequenceNumber"
                ON "RoutePoints" ("RouteId", "SequenceNumber");
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_RoutePoints_OrderId" ON "RoutePoints" ("OrderId");
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RoutePoints_OrderId_Active"
                ON "RoutePoints" ("OrderId") WHERE "IsActive" = 1;
            """);
    }

    private static void EnsureRouteAcceptedAtColumn(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            conn.Open();
        }

        try
        {
            var hasColumn = false;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info('Routes');";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "AcceptedAt", StringComparison.OrdinalIgnoreCase))
                    {
                        hasColumn = true;
                        break;
                    }
                }
            }

            if (!hasColumn)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE \"Routes\" ADD COLUMN \"AcceptedAt\" TEXT NULL;";
                alter.ExecuteNonQuery();
            }
        }
        finally
        {
            if (shouldClose)
            {
                conn.Close();
            }
        }
    }
}
