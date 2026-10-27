namespace DeliverySystem.Core.Intake;

public interface IOrderIntakeSource
{
    Task<IntakeResult> HandleEventAsync(LeadEvent evt, CancellationToken ct = default);
}
