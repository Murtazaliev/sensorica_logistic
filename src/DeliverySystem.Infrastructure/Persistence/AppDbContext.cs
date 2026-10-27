using DeliverySystem.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace DeliverySystem.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Point> Points => Set<Point>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<WebhookLog> WebhookLogs => Set<WebhookLog>();
    public DbSet<SyncOperation> SyncOperations => Set<SyncOperation>();
    public DbSet<SyncChange> SyncChanges => Set<SyncChange>();
    public DbSet<Courier> Couriers => Set<Courier>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<RoutePoint> RoutePoints => Set<RoutePoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasConversion<string>();
            e.Property(x => x.PreferredLanguage).HasMaxLength(10).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Role });
        });

        modelBuilder.Entity<Point>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Address).HasMaxLength(500).IsRequired();
            e.Property(x => x.ApprovalStatus).HasConversion<string>();
            e.HasIndex(x => new { x.TenantId, x.ExternalId })
                .IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.DeferredReason).HasConversion<string>();
            e.Property(x => x.ValidationError).HasConversion<string>();
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.TotalWeight).HasPrecision(10, 3);
            e.Property(x => x.TotalVolume).HasPrecision(12, 4);
            e.HasIndex(x => new { x.TenantId, x.AmoLeadId })
                .IsUnique()
                .HasFilter("\"AmoLeadId\" IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.Status, x.RequestedDate });
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.StatusHistory).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Point).WithMany().HasForeignKey(x => x.PointId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductName).HasMaxLength(300).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(20).IsRequired();
            e.Property(x => x.Weight).HasPrecision(10, 3);
            e.Property(x => x.Quantity).HasPrecision(10, 3);
        });

        modelBuilder.Entity<OrderStatusHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FromStatus).HasConversion<string>();
            e.Property(x => x.ToStatus).HasConversion<string>();
            e.HasIndex(x => new { x.OrderId, x.ChangedAt });
        });

        modelBuilder.Entity<WebhookLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasConversion<string>();
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            e.Property(x => x.ExternalEventId).HasMaxLength(128).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.ExternalEventId });
        });

        modelBuilder.Entity<SyncOperation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DeviceId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Type).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.PayloadJson).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.ClientOperationId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.AppliedAt });
        });

        modelBuilder.Entity<SyncChange>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.EntityType).HasConversion<string>();
            e.Property(x => x.ChangeType).HasConversion<string>();
            e.Property(x => x.PayloadJson).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Id });
        });

        modelBuilder.Entity<Courier>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>();
            e.Property(x => x.VehicleType).HasConversion<string>();
            e.Property(x => x.CapacityWeight).HasPrecision(10, 3);
            e.Property(x => x.CapacityVolume).HasPrecision(10, 3);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Route>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.TotalWeight).HasPrecision(10, 3);
            e.Property(x => x.TotalVolume).HasPrecision(10, 3);
            e.Property(x => x.TotalDistance).HasPrecision(10, 2);
            e.HasIndex(x => new { x.CourierId, x.Date }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Date });
            e.HasOne(x => x.Courier).WithMany().HasForeignKey(x => x.CourierId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Points).WithOne().HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RoutePoint>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => new { x.RouteId, x.SequenceNumber });
            e.HasIndex(x => x.OrderId).IsUnique().HasFilter("\"IsActive\" = 1");
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
