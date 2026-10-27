using DeliverySystem.Core.Domain;

namespace DeliverySystem.Api.Components;

public static class RouteDisplay
{
    public static string Label(RouteStatus status) => status switch
    {
        RouteStatus.Draft => "Черновик",
        RouteStatus.Confirmed => "Подтверждён",
        RouteStatus.InProgress => "В работе",
        RouteStatus.Completed => "Завершён",
        RouteStatus.Cancelled => "Отменён",
        _ => status.ToString()
    };

    public static string CssClass(RouteStatus status) => status switch
    {
        RouteStatus.Draft => "status-scheduled",
        RouteStatus.Confirmed => "status-accepted",
        RouteStatus.InProgress => "status-intransit",
        RouteStatus.Completed => "status-delivered",
        RouteStatus.Cancelled => "status-cancelled",
        _ => "status-new"
    };

    public static string Label(VehicleType type) => type switch
    {
        VehicleType.Car => "Автомобиль",
        VehicleType.Motorcycle => "Мотоцикл",
        VehicleType.Bicycle => "Велосипед",
        VehicleType.OnFoot => "Пешком",
        _ => type.ToString()
    };

    public static string Label(CourierType type) => type switch
    {
        CourierType.Staff => "Штатный",
        CourierType.Contractor => "Подрядчик",
        CourierType.SelfEmployed => "Самозанятый",
        _ => type.ToString()
    };

    public static string Label(RoutePointStatus status) => status switch
    {
        RoutePointStatus.Pending => "Ожидает",
        RoutePointStatus.Arrived => "На месте",
        RoutePointStatus.Delivered => "Доставлено",
        RoutePointStatus.Problem => "Проблема",
        RoutePointStatus.Cancelled => "Отменено",
        RoutePointStatus.Reassigned => "Переназначено",
        _ => status.ToString()
    };
}
