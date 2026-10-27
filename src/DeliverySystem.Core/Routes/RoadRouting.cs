namespace DeliverySystem.Core.Routes;

public sealed record GeoCoordinate(double Latitude, double Longitude);

public sealed record RoadRoute(
    double DistanceMeters,
    int DurationSeconds,
    IReadOnlyList<GeoCoordinate> Geometry);

public interface IRoadRouter
{
    Task<RoadRoute?> BuildAsync(
        IReadOnlyList<GeoCoordinate> points,
        CancellationToken ct = default);
}

public sealed record RoadDistanceMatrix(int[,] Distances, int[,] Durations);

public interface IRoadDistanceMatrix
{
    Task<RoadDistanceMatrix?> BuildAsync(
        IReadOnlyList<GeoCoordinate> points,
        CancellationToken ct = default);
}

public sealed record DepotOptions(
    string Name,
    string? Address,
    double? Latitude,
    double? Longitude);
