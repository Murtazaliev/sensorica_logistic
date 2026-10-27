namespace DeliverySystem.Core.Domain;

public static class OrderStatusTransitions
{
    public static bool IsAllowed(OrderStatus from, OrderStatus to) => to switch
    {
        OrderStatus.Scheduled => from == OrderStatus.New,
        OrderStatus.Accepted => from is OrderStatus.New or OrderStatus.Scheduled,
        OrderStatus.InTransit => from is OrderStatus.New or OrderStatus.Scheduled or OrderStatus.Accepted,
        OrderStatus.Delivered => from == OrderStatus.InTransit,
        OrderStatus.Completed => from == OrderStatus.Delivered,
        OrderStatus.Problem => from == OrderStatus.InTransit,
        OrderStatus.Cancelled => from is OrderStatus.New or OrderStatus.Scheduled
            or OrderStatus.Accepted or OrderStatus.InTransit,
        _ => false
    };
}
