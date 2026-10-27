using DeliverySystem.Core.Intake;

namespace DeliverySystem.Infrastructure.Intake;

public sealed class MockAmoIntake(OrderIntakeService service, AmoFixtureRepository fixtures) : IOrderIntakeSource
{
    public Task<IntakeResult> HandleEventAsync(LeadEvent evt, CancellationToken ct = default)
        => service.HandleEventAsync(evt, ct);

    public async Task<IntakeResult> RunScenarioAsync(
        string scenario,
        Guid tenantId,
        DateTimeOffset occurredAt,
        long? leadIdOverride = null,
        CancellationToken ct = default)
    {
        var evt = await fixtures.BuildEventAsync(scenario, tenantId, occurredAt, leadIdOverride, ct);
        return await service.HandleEventAsync(evt, ct);
    }
}
