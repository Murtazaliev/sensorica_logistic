using DeliverySystem.Core.Domain;

namespace DeliverySystem.Core.Intake;

public sealed record IntakeResult(
    IntakeStatus Status,
    Guid? OrderId,
    string? Reason);
