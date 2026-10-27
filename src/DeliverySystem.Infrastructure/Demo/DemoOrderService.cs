using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Intake;
using DeliverySystem.Infrastructure.Intake;
using DeliverySystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliverySystem.Infrastructure.Demo;

public sealed class DemoOrderService(
    AppDbContext db,
    OrderIntakeService intake,
    TimeProvider clock)
{
    private static readonly long[] PointExternalIds = [7001, 7003, 7004];

    private static readonly (string Product, decimal Weight)[] Items =
    [
        ("Сырники", 5),
        ("Вареники", 8),
        ("Пельмени", 3),
        ("Блины", 6)
    ];

    public async Task<int> CreateForDateAsync(
        Guid tenantId,
        DateOnly date,
        int count = 4,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (date < today)
        {
            throw new ArgumentException("Дата демо-заказов не может быть в прошлом.");
        }

        count = Math.Clamp(count, 1, 10);

        var maxLeadId = await db.Orders
            .Where(o => o.TenantId == tenantId)
            .MaxAsync(o => (long?)o.AmoLeadId, ct) ?? 9000;

        var now = clock.GetUtcNow();
        var created = 0;

        for (var i = 0; i < count; i++)
        {
            var item = Items[i % Items.Length];
            var evt = new LeadEvent(
                tenantId,
                "update_lead",
                maxLeadId + i + 1,
                1,
                100,
                "Готов к доставке",
                null,
                PointExternalIds[i % PointExternalIds.Length],
                date,
                [new LeadItem(item.Product, item.Weight, "kg", item.Weight)],
                "{}",
                now);

            var result = await intake.HandleEventAsync(evt, ct);
            if (result.OrderId is not null)
            {
                created++;
            }
        }

        return created;
    }
}
