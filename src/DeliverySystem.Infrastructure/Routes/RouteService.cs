using System.Text.Json;
using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Routes;
using DeliverySystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliverySystem.Infrastructure.Routes;

public sealed class RouteService
{
    private readonly AppDbContext db;
    private readonly TimeProvider clock;
    private readonly DepotOptions? depot;
    private readonly IRoadRouter? roadRouter;
    private readonly IRoadDistanceMatrix? roadMatrix;

    public RouteService(AppDbContext db, TimeProvider clock)
        : this(db, clock, null, null, null)
    {
    }

    public RouteService(
        AppDbContext db,
        TimeProvider clock,
        DepotOptions? depot,
        IRoadRouter? roadRouter)
        : this(db, clock, depot, roadRouter, null)
    {
    }

    public RouteService(
        AppDbContext db,
        TimeProvider clock,
        DepotOptions? depot,
        IRoadRouter? roadRouter,
        IRoadDistanceMatrix? roadMatrix)
    {
        this.db = db;
        this.clock = clock;
        this.depot = depot;
        this.roadRouter = roadRouter;
        this.roadMatrix = roadMatrix;
    }

    public async Task<IReadOnlyList<CourierDto>> ListCouriersAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await db.Couriers
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .Include(c => c.User)
            .OrderBy(c => c.User!.FullName)
            .Select(c => new CourierDto(
                c.Id,
                c.User!.FullName,
                c.User.Phone,
                c.Type,
                c.VehicleType,
                c.VehiclePlate,
                c.CapacityWeight,
                c.MaxPointsPerDay,
                c.IsAvailable))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RouteSummaryDto>> ListRoutesAsync(
        Guid tenantId,
        DateOnly date,
        CancellationToken ct = default)
    {
        return await db.Routes
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Date == date)
            .Include(r => r.Courier)
            .ThenInclude(c => c!.User)
            .OrderBy(r => r.Courier!.User!.FullName)
            .Select(r => new RouteSummaryDto(
                r.Id,
                r.CourierId,
                r.Courier!.User!.FullName,
                r.Date,
                r.Status,
                r.IsLocked,
                r.TotalPoints,
                r.TotalWeight))
            .ToListAsync(ct);
    }

    public async Task<RouteDetailsDto?> GetAsync(
        Guid tenantId,
        Guid routeId,
        CancellationToken ct = default)
    {
        var route = await db.Routes
            .AsNoTracking()
            .Include(r => r.Courier)
            .ThenInclude(c => c!.User)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == routeId, ct);

        if (route is null)
        {
            return null;
        }

        return await BuildDetailsAsync(route, tenantId, ct);
    }

    public async Task<RouteDetailsDto?> GetForCourierAsync(
        Guid tenantId,
        Guid courierId,
        DateOnly date,
        CancellationToken ct = default)
    {
        var route = await db.Routes
            .AsNoTracking()
            .Include(r => r.Courier)
            .ThenInclude(c => c!.User)
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.CourierId == courierId && r.Date == date,
                ct);

        if (route is null)
        {
            return null;
        }

        return await BuildDetailsAsync(route, tenantId, ct);
    }

    public async Task<bool> IsPlanningLockedAsync(
        Guid tenantId,
        DateOnly date,
        CancellationToken ct = default)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return true;
        }

        var localNow = GetTenantNow(tenant);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var localTime = TimeOnly.FromDateTime(localNow.DateTime);

        return date < today || (date == today && localTime >= tenant.PlanningLockTime);
    }

    public async Task<RouteDetailsDto> EnsureDraftAsync(
        Guid tenantId,
        Guid courierId,
        DateOnly date,
        CancellationToken ct = default)
    {
        var courierExists = await db.Couriers.AnyAsync(
            c => c.TenantId == tenantId && c.Id == courierId, ct);

        if (!courierExists)
        {
            throw new InvalidOperationException("Курьер не найден.");
        }

        var existing = await db.Routes
            .AsNoTracking()
            .Include(r => r.Courier)
            .ThenInclude(c => c!.User)
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.CourierId == courierId && r.Date == date,
                ct);

        if (existing is not null)
        {
            return await BuildDetailsAsync(existing, tenantId, ct);
        }

        if (await IsPlanningLockedAsync(tenantId, date, ct))
        {
            throw new InvalidOperationException("Планирование на эту дату заблокировано после 10:00.");
        }

        var route = new Route
        {
            TenantId = tenantId,
            CourierId = courierId,
            Date = date,
            Status = RouteStatus.Draft,
            PlannedStartTime = new TimeOnly(10, 0),
            CreatedAt = clock.GetUtcNow(),
            UpdatedAt = clock.GetUtcNow()
        };

        db.Routes.Add(route);
        await db.SaveChangesAsync(ct);

        var saved = await db.Routes
            .AsNoTracking()
            .Include(r => r.Courier)
            .ThenInclude(c => c!.User)
            .FirstAsync(r => r.Id == route.Id, ct);

        return await BuildDetailsAsync(saved, tenantId, ct);
    }

    public async Task<RouteDetailsDto> SetStopsAsync(
        Guid tenantId,
        Guid routeId,
        IReadOnlyList<Guid> orderIds,
        bool byAlgorithm = false,
        CancellationToken ct = default)
    {
        var route = await LoadEditableRouteAsync(tenantId, routeId, ct);

        if (await IsPlanningLockedAsync(tenantId, route.Date, ct))
        {
            throw new InvalidOperationException("Планирование на эту дату заблокировано после 10:00.");
        }

        if (orderIds.Distinct().Count() != orderIds.Count)
        {
            throw new ArgumentException("В маршруте не должно быть повторяющихся заказов.");
        }

        var orders = await db.Orders
            .Include(o => o.Point)
            .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
            .ToListAsync(ct);

        if (orders.Count != orderIds.Count)
        {
            throw new InvalidOperationException("Часть заказов не найдена.");
        }

        var byId = orders.ToDictionary(o => o.Id);
        var orderedOrders = new List<Order>();

        foreach (var orderId in orderIds)
        {
            var order = byId[orderId];
            if (order.RequestedDate != route.Date)
            {
                throw new InvalidOperationException($"Заказ {order.OrderNumber} не относится к дате маршрута.");
            }

            if (order.Status is OrderStatus.Cancelled or OrderStatus.Rejected)
            {
                throw new InvalidOperationException($"Заказ {order.OrderNumber} отменён.");
            }

            if (order.IsBlockedByPointApproval)
            {
                throw new InvalidOperationException($"Заказ {order.OrderNumber} заблокирован точкой на модерации.");
            }

            if (order.Point?.Latitude is null || order.Point.Longitude is null)
            {
                throw new InvalidOperationException($"У точки заказа {order.OrderNumber} не заданы координаты.");
            }

            orderedOrders.Add(order);
        }

        var conflictingOrderIds = await (
                from rp in db.RoutePoints
                join r in db.Routes on rp.RouteId equals r.Id
                where rp.IsActive
                      && rp.RouteId != route.Id
                      && r.TenantId == tenantId
                      && r.Status != RouteStatus.Cancelled
                      && orderIds.Contains(rp.OrderId)
                select rp.OrderId)
            .ToListAsync(ct);

        if (conflictingOrderIds.Count > 0)
        {
            throw new InvalidOperationException("Часть заказов уже включена в другой маршрут.");
        }

        var previousPoints = await db.RoutePoints
            .Where(p => p.RouteId == route.Id && p.IsActive)
            .ToListAsync(ct);

        foreach (var point in previousPoints)
        {
            point.IsActive = false;
        }

        await db.SaveChangesAsync(ct);

        var now = clock.GetUtcNow();
        var sequence = 1;

        foreach (var order in orderedOrders)
        {
            var arrivalFrom = route.PlannedStartTime.AddMinutes(30 * (sequence - 1));
            var arrivalTo = arrivalFrom.AddMinutes(30);

            db.RoutePoints.Add(new RoutePoint
            {
                RouteId = route.Id,
                OrderId = order.Id,
                SequenceNumber = sequence,
                PlannedArrivalFrom = arrivalFrom,
                PlannedArrivalTo = arrivalTo,
                Status = RoutePointStatus.Pending,
                IsActive = true
            });

            sequence++;
        }

        var routeCoordinates = BuildRouteCoordinates(orderedOrders);
        RoadRoute? roadRoute = null;

        if (roadRouter is not null && routeCoordinates.Count >= 2)
        {
            try
            {
                roadRoute = await roadRouter.BuildAsync(routeCoordinates, ct);
            }
            catch
            {
                // Routing API unavailable — fall back to straight lines.
            }
        }

        route.TotalPoints = orderedOrders.Count;
        route.TotalWeight = orderedOrders.Sum(o => o.TotalWeight);
        route.TotalVolume = orderedOrders.Sum(o => o.TotalVolume);
        route.TotalDistance = roadRoute is not null
            ? Math.Round((decimal)roadRoute.DistanceMeters / 1000m, 2, MidpointRounding.AwayFromZero)
            : CalculateDistanceKm(routeCoordinates);
        route.TotalDuration = roadRoute is not null
            ? Math.Max(1, (int)Math.Round(roadRoute.DurationSeconds / 60.0))
            : orderedOrders.Count * 30;
        route.BuiltByAlgorithm = byAlgorithm;
        route.UpdatedAt = now;
        route.Geometry = roadRoute is not null
            ? SerializeGeometry(roadRoute.Geometry)
            : SerializeGeometry(routeCoordinates);

        await db.SaveChangesAsync(ct);

        var saved = await db.Routes
            .AsNoTracking()
            .Include(r => r.Courier)
            .ThenInclude(c => c!.User)
            .FirstAsync(r => r.Id == route.Id, ct);

        return await BuildDetailsAsync(saved, tenantId, ct);
    }

    public async Task<RouteDetailsDto> AutoOrderAsync(
        Guid tenantId,
        Guid routeId,
        CancellationToken ct = default)
    {
        var route = await LoadEditableRouteAsync(tenantId, routeId, ct);
        var points = await db.RoutePoints
            .AsNoTracking()
            .Where(p => p.RouteId == route.Id && p.IsActive)
            .Include(p => p.Order)
            .ThenInclude(o => o!.Point)
            .OrderBy(p => p.SequenceNumber)
            .ToListAsync(ct);

        if (points.Count < 2)
        {
            return (await GetAsync(tenantId, routeId, ct))!;
        }

        var roadOrder = await TryAutoOrderByRoadAsync(points, ct);
        if (roadOrder is not null)
        {
            return await SetStopsAsync(tenantId, routeId, roadOrder, byAlgorithm: true, ct);
        }

        var remaining = points.ToList();
        var ordered = new List<RoutePoint> { remaining[0] };
        remaining.RemoveAt(0);

        while (remaining.Count > 0)
        {
            var current = ordered[^1].Order!.Point!;
            var next = remaining
                .OrderBy(p => HaversineKm(
                    current.Latitude!.Value,
                    current.Longitude!.Value,
                    p.Order!.Point!.Latitude!.Value,
                    p.Order.Point.Longitude!.Value))
                .First();

            ordered.Add(next);
            remaining.Remove(next);
        }

        return await SetStopsAsync(
            tenantId,
            routeId,
            ordered.Select(p => p.OrderId).ToList(),
            byAlgorithm: true,
            ct);
    }

    private async Task<IReadOnlyList<Guid>?> TryAutoOrderByRoadAsync(
        IReadOnlyList<RoutePoint> points,
        CancellationToken ct)
    {
        if (roadMatrix is null)
        {
            return null;
        }

        var orders = points.Select(p => p.Order).OfType<Order>().ToList();
        if (orders.Count != points.Count)
        {
            return null;
        }

        var coordinates = BuildRouteCoordinates(orders);
        if (coordinates.Count < 2)
        {
            return null;
        }

        RoadDistanceMatrix? matrix;
        try
        {
            matrix = await roadMatrix.BuildAsync(coordinates, ct);
        }
        catch
        {
            return null;
        }

        if (matrix is null)
        {
            return null;
        }

        var size = matrix.Distances.GetLength(0);
        if (size != coordinates.Count || matrix.Distances.GetLength(1) != size)
        {
            return null;
        }

        var depotOffset = coordinates.Count - orders.Count;
        var orderIndices = SolveOpenPath(matrix.Distances);
        var result = new List<Guid>();

        foreach (var index in orderIndices)
        {
            if (index < depotOffset)
            {
                continue;
            }

            var orderIndex = index - depotOffset;
            if (orderIndex < 0 || orderIndex >= orders.Count)
            {
                return null;
            }

            result.Add(orders[orderIndex].Id);
        }

        return result.Count == orders.Count ? result : null;
    }

    private static List<int> SolveOpenPath(int[,] distances)
    {
        var size = distances.GetLength(0);
        var route = new List<int> { 0 };
        var visited = new bool[size];
        visited[0] = true;

        while (route.Count < size)
        {
            var last = route[^1];
            var next = -1;
            var best = int.MaxValue;

            for (var i = 0; i < size; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                var cost = distances[last, i];
                if (cost >= 0 && cost < best)
                {
                    best = cost;
                    next = i;
                }
            }

            if (next < 0)
            {
                return route;
            }

            route.Add(next);
            visited[next] = true;
        }

        var improved = true;
        var guard = 0;

        while (improved && guard++ < 20)
        {
            improved = false;
            var bestCost = RouteCost(route, distances);

            for (var from = 1; from < size; from++)
            {
                for (var to = 1; to < size; to++)
                {
                    if (from == to)
                    {
                        continue;
                    }

                    var candidate = new List<int>(route);
                    var node = candidate[from];
                    candidate.RemoveAt(from);
                    candidate.Insert(to, node);

                    var cost = RouteCost(candidate, distances);
                    if (cost < bestCost)
                    {
                        route = candidate;
                        bestCost = cost;
                        improved = true;
                    }
                }
            }
        }

        return route;
    }

    private static int RouteCost(IReadOnlyList<int> route, int[,] distances)
    {
        var cost = 0;

        for (var i = 0; i < route.Count - 1; i++)
        {
            cost += distances[route[i], route[i + 1]];
        }

        return cost;
    }

    public async Task<RouteDetailsDto> ConfirmAsync(
        Guid tenantId,
        Guid routeId,
        CancellationToken ct = default)
    {
        var route = await LoadEditableRouteAsync(tenantId, routeId, ct);

        if (route.TotalPoints == 0)
        {
            throw new InvalidOperationException("Нельзя подтвердить пустой маршрут.");
        }

        if (await IsPlanningLockedAsync(tenantId, route.Date, ct))
        {
            throw new InvalidOperationException("Планирование на эту дату заблокировано после 10:00.");
        }

        var now = clock.GetUtcNow();
        route.Status = RouteStatus.Confirmed;
        route.IsLocked = true;
        route.LockedAt = now;
        route.ConfirmedAt = now;
        route.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return (await GetAsync(tenantId, routeId, ct))!;
    }

    public async Task<IReadOnlyList<AvailableOrderDto>> ListAvailableOrdersAsync(
        Guid tenantId,
        DateOnly date,
        CancellationToken ct = default)
    {
        var assignedOrderIds = await (
                from rp in db.RoutePoints
                join r in db.Routes on rp.RouteId equals r.Id
                where rp.IsActive
                      && r.TenantId == tenantId
                      && r.Status != RouteStatus.Cancelled
                select rp.OrderId)
            .ToListAsync(ct);

        return await db.Orders
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId
                        && o.RequestedDate == date
                        && o.Status != OrderStatus.Cancelled
                        && o.Status != OrderStatus.Rejected
                        && !o.IsBlockedByPointApproval
                        && o.PointId != null
                        && !assignedOrderIds.Contains(o.Id))
            .Include(o => o.Point)
            .OrderBy(o => o.OrderNumber)
            .Select(o => new AvailableOrderDto(
                o.Id,
                o.OrderNumber,
                o.Point!.Name,
                o.Point.Address,
                o.Point.Latitude,
                o.Point.Longitude,
                o.TotalWeight,
                o.Status))
            .ToListAsync(ct);
    }

    private async Task<Route> LoadEditableRouteAsync(
        Guid tenantId,
        Guid routeId,
        CancellationToken ct)
    {
        var route = await db.Routes.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.Id == routeId, ct);

        if (route is null)
        {
            throw new InvalidOperationException("Маршрут не найден.");
        }

        if (route.Status != RouteStatus.Draft)
        {
            throw new InvalidOperationException("Маршрут уже подтверждён и не редактируется.");
        }

        return route;
    }

    private async Task<RouteDetailsDto> BuildDetailsAsync(
        Route route,
        Guid tenantId,
        CancellationToken ct)
    {
        var points = await db.RoutePoints
            .AsNoTracking()
            .Where(p => p.RouteId == route.Id && p.IsActive)
            .Include(p => p.Order)
            .ThenInclude(o => o!.Point)
            .OrderBy(p => p.SequenceNumber)
            .ToListAsync(ct);

        var stops = points
            .Where(p => p.Order is not null)
            .Select(p => new RouteStopDto(
                p.Id,
                p.OrderId,
                p.SequenceNumber,
                p.Order!.OrderNumber,
                p.Order.Point?.Name ?? "—",
                p.Order.Point?.Address ?? "—",
                p.Order.Point?.Latitude,
                p.Order.Point?.Longitude,
                p.Order.TotalWeight,
                p.Order.Status))
            .ToList();

        return new RouteDetailsDto(
            route.Id,
            route.CourierId,
            route.Courier?.User?.FullName ?? "Курьер",
            route.Date,
            route.Status,
            route.IsLocked,
            await IsPlanningLockedAsync(tenantId, route.Date, ct),
            route.TotalPoints,
            route.TotalWeight,
            route.TotalDistance,
            route.TotalDuration,
            ParseGeometry(route.Geometry),
            stops);
    }

    private DateTimeOffset GetTenantNow(Tenant tenant)
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZoneId);
            return TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return clock.GetUtcNow();
        }
        catch (InvalidTimeZoneException)
        {
            return clock.GetUtcNow();
        }
    }

    private List<GeoCoordinate> BuildRouteCoordinates(IReadOnlyList<Order> orders)
    {
        var coordinates = new List<GeoCoordinate>();

        if (depot?.Latitude is not null && depot.Longitude is not null)
        {
            coordinates.Add(new GeoCoordinate(depot.Latitude.Value, depot.Longitude.Value));
        }

        foreach (var order in orders)
        {
            if (order.Point?.Latitude is not null && order.Point.Longitude is not null)
            {
                coordinates.Add(new GeoCoordinate(
                    order.Point.Latitude.Value,
                    order.Point.Longitude.Value));
            }
        }

        return coordinates;
    }

    private static decimal CalculateDistanceKm(IReadOnlyList<GeoCoordinate> coordinates)
    {
        var distance = 0d;

        for (var i = 1; i < coordinates.Count; i++)
        {
            distance += HaversineKm(
                coordinates[i - 1].Latitude,
                coordinates[i - 1].Longitude,
                coordinates[i].Latitude,
                coordinates[i].Longitude);
        }

        return Math.Round((decimal)distance, 2);
    }

    private static string? SerializeGeometry(IReadOnlyList<GeoCoordinate> coordinates)
    {
        if (coordinates.Count < 2)
        {
            return null;
        }

        return JsonSerializer.Serialize(coordinates.Select(c => new[]
        {
            c.Latitude,
            c.Longitude
        }));
    }

    private static IReadOnlyList<RouteCoordinateDto> ParseGeometry(string? geometry)
    {
        if (string.IsNullOrWhiteSpace(geometry))
        {
            return [];
        }

        try
        {
            var raw = JsonSerializer.Deserialize<double[][]>(geometry);
            if (raw is null)
            {
                return [];
            }

            return raw
                .Where(pair => pair.Length >= 2)
                .Select(pair => new RouteCoordinateDto(pair[0], pair[1]))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371.0;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
