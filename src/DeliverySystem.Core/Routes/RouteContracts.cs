using DeliverySystem.Core.Domain;

namespace DeliverySystem.Core.Routes;

public sealed record CourierDto(
    Guid Id,
    string Name,
    string? Phone,
    CourierType Type,
    VehicleType VehicleType,
    string? VehiclePlate,
    decimal CapacityWeight,
    int? MaxPointsPerDay,
    bool IsAvailable);

public sealed record RouteSummaryDto(
    Guid Id,
    Guid CourierId,
    string CourierName,
    DateOnly Date,
    RouteStatus Status,
    bool IsLocked,
    int TotalPoints,
    decimal TotalWeight);

public sealed record RouteStopDto(
    Guid Id,
    Guid OrderId,
    int SequenceNumber,
    string OrderNumber,
    string PointName,
    string Address,
    double? Latitude,
    double? Longitude,
    decimal Weight,
    TimeOnly? PlannedArrivalFrom,
    TimeOnly? PlannedArrivalTo,
    OrderStatus OrderStatus);

public sealed record RouteDetailsDto(
    Guid Id,
    Guid CourierId,
    string CourierName,
    DateOnly Date,
    RouteStatus Status,
    bool IsLocked,
    bool IsPlanningLocked,
    int TotalPoints,
    decimal TotalWeight,
    decimal TotalDistance,
    int TotalDuration,
    IReadOnlyList<RouteCoordinateDto> RoadGeometry,
    List<RouteStopDto> Stops);

public sealed record RouteCoordinateDto(double Latitude, double Longitude);

public sealed record AvailableOrderDto(
    Guid OrderId,
    string OrderNumber,
    string PointName,
    string Address,
    double? Latitude,
    double? Longitude,
    decimal Weight,
    OrderStatus Status);

public sealed record CreateRouteRequest(Guid CourierId, DateOnly Date);

public sealed record SetRouteStopsRequest(List<Guid> OrderIds);
