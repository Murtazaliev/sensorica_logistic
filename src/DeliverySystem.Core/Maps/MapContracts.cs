namespace DeliverySystem.Core.Maps;

public sealed record GeocodePlace(
    string FullName,
    string? AddressName,
    double Latitude,
    double Longitude);

public sealed record AddressSuggestion(
    string Name,
    string FullName,
    string? Type);
