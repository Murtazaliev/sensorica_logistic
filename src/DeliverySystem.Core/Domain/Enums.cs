namespace DeliverySystem.Core.Domain;

public enum OrderStatus
{
    New,
    Scheduled,
    Accepted,
    InTransit,
    Delivered,
    Completed,
    Problem,
    Cancelled,
    Rejected
}

public enum OrderDeferredReason
{
    AfterCutoff,
    InvalidDate,
    Manual
}

public enum OrderValidationError
{
    InvalidDate,
    MissingItems,
    UnknownPoint
}

public enum PointApprovalStatus
{
    Active,
    PendingApproval,
    Rejected
}

public enum UserRole
{
    Admin,
    Dispatcher,
    Courier,
    PointManager,
    SalesManager,
    Head
}

public enum IntakeStatus
{
    Created,
    Updated,
    Deferred,
    Duplicate,
    Cancelled,
    ValidationError
}

public enum WebhookSource
{
    AmoCRM,
    Edo,
    Maps
}
