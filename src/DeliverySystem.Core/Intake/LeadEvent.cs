namespace DeliverySystem.Core.Intake;

public sealed record LeadItem(
    string ProductName,
    decimal Quantity,
    string Unit,
    decimal Weight);

public sealed record LeadEvent(
    Guid TenantId,
    string EventType,
    long AmoLeadId,
    long PipelineId,
    long StageId,
    string? StageName,
    long? ResponsibleAmoUserId,
    long? PointExternalId,
    DateOnly? RequestedDate,
    IReadOnlyList<LeadItem> Items,
    string RawJson,
    DateTimeOffset OccurredAt);
