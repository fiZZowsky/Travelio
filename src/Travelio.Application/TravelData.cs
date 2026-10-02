using Travelio.Domain;

namespace Travelio.Application;

public sealed record SourcedData<T>(T Value, string Source, string SourceUrl, DateTimeOffset RetrievedAt, bool IsStale = false);
public sealed record Accommodation(string Id, string Name, string Address, Coordinate Location, string SourceUrl, string? Website);
public sealed record PlaceCollection(Attraction[] Attractions, Accommodation[] Accommodations);
public sealed record WalkingRoute(Coordinate[] Geometry, double Kilometers, int Minutes);
public sealed record ExchangeRate(string Currency, decimal RateToPln, DateOnly EffectiveDate);
public interface ITravelDataProvider
{
    Task<SourcedData<Destination[]>> SearchCitiesAsync(string query, CancellationToken ct = default);
    Task<Destination> GetCityAsync(string id, CancellationToken ct = default);
    Task<SourcedData<PlaceCollection>> GetPlacesAsync(Coordinate center, CancellationToken ct = default);
    Task<SourcedData<Attraction[]>> SearchPlacesAsync(string query, Coordinate center, CancellationToken ct = default);
    Task<SourcedData<WalkingRoute>> GetRouteAsync(Coordinate[] points, CancellationToken ct = default);
    Task<SourcedData<ExchangeRate>> GetRateAsync(string currency, DateOnly date, CancellationToken ct = default);
}
public sealed class TravelDataUnavailableException(string message) : Exception(message);
