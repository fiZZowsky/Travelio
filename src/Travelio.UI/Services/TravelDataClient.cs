using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Travelio.Application;
using Travelio.Domain;

namespace Travelio.UI.Services;

/// <summary>Bounded offline cache with source timestamps. A failed fetch never becomes an empty success.</summary>
public sealed class TravelDataClient(HttpClient http, ILocalStore store, IDestinationCatalog catalog)
{
    private static string N(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
    private static string Position(Coordinate p) => $"lat={N(p.Latitude)}&lon={N(p.Longitude)}";
    private readonly SemaphoreSlim _cacheGate = new(1);
    public Task<SourcedData<Destination[]>> SearchCitiesAsync(string query) =>
        Fetch<Destination[]>($"cities?q={Uri.EscapeDataString(query.Trim())}", TimeSpan.FromDays(7));
    public Task<SourcedData<PlaceCollection>> PlacesAsync(Destination destination) =>
        Fetch<PlaceCollection>($"places?{Position(destination.Location)}", TimeSpan.FromHours(24));
    public Task<SourcedData<Attraction[]>> SearchPlacesAsync(string query, Destination destination) =>
        Fetch<Attraction[]>($"search?q={Uri.EscapeDataString(query.Trim())}&{Position(destination.Location)}", TimeSpan.FromDays(7));
    public Task<SourcedData<WalkingRoute>> RouteAsync(IReadOnlyList<PlanStop> stops) =>
        Fetch<WalkingRoute>("route?points=" + Uri.EscapeDataString(string.Join(';', stops.Select(s => $"{N(s.Latitude)},{N(s.Longitude)}"))), TimeSpan.FromDays(7));
    public Task<SourcedData<ExchangeRate>> RateAsync(string currency, DateOnly date) =>
        Fetch<ExchangeRate>($"rates/{Uri.EscapeDataString(currency)}?date={date:yyyy-MM-dd}", TimeSpan.FromHours(6));
    public async Task<Destination> GetCityAsync(string id)
    {
        var saved = await store.GetAsync<Destination>("travelio.city." + id);
        if (saved is not null) { catalog.Register(saved); return saved; }
        try { return catalog.Get(id); }
        catch (DomainException) { }
        var city = await http.GetFromJsonAsync<Destination>("api/data/cities/" + Uri.EscapeDataString(id))
            ?? throw new DomainException("Nie znaleziono miasta.");
        await RememberAsync(city);
        return city;
    }
    public async Task RememberAsync(Destination destination)
    {
        await _cacheGate.WaitAsync();
        try
        {
            await store.SetAsync("travelio.city." + destination.Id, destination);
            var ids = await store.GetAsync<List<string>>("travelio.city.index") ?? [];
            ids.Remove(destination.Id); ids.Insert(0, destination.Id);
            foreach (var oldId in ids.Skip(40)) await store.RemoveAsync("travelio.city." + oldId);
            await store.SetAsync("travelio.city.index", ids.Take(40).ToList());
            catalog.Register(destination);
        }
        finally { _cacheGate.Release(); }
    }
    public async Task<IReadOnlyList<Destination>> RecentCitiesAsync()
    {
        var result = new List<Destination>();
        foreach (var id in await store.GetAsync<List<string>>("travelio.city.index") ?? [])
            if (await store.GetAsync<Destination>("travelio.city." + id) is { } city) result.Add(city);
        return result;
    }
    public async Task<Destination> EnrichAsync(Destination destination)
    {
        var result = await PlacesAsync(destination);
        var updated = destination with { Attractions = result.Value.Attractions, SourceUrl = result.SourceUrl, RetrievedAt = result.RetrievedAt };
        await RememberAsync(updated);
        return updated;
    }
    public async Task<(Destination Destination, string? Notice)> ForPlanningAsync(Destination destination, bool serverAvailable)
    {
        if (serverAvailable)
        {
            try
            {
                var places = await PlacesAsync(destination);
                if (places.Value.Attractions.Length > 0)
                {
                    var updated = destination with { Attractions = places.Value.Attractions, SourceUrl = places.SourceUrl, RetrievedAt = places.RetrievedAt };
                    await RememberAsync(updated);
                    return (updated, places.IsStale ? "Plan ułożono z wcześniej pobranych miejsc. Sprawdź aktualne godziny otwarcia przed wyjazdem." : null);
                }
            }
            catch (DomainException) { /* A provider outage must not prevent local planning. */ }
        }
        var saved = await store.GetAsync<Destination>("travelio.city." + destination.Id) ?? destination;
        catalog.Register(saved);
        return (saved, saved.Attractions.Length > 0
            ? "Plan ułożono z miejsc zapisanych na urządzeniu. Aktualizacja katalogu jest teraz niedostępna."
            : "Podróż zapisana lokalnie z pustym planem. Pobierz miejsca po odzyskaniu połączenia z serwerem lub dodaj zapisane punkty.");
    }
    private async Task<SourcedData<T>> Fetch<T>(string path, TimeSpan ttl)
    {
        var key = "travelio.data." + path;
        var saved = await store.GetAsync<SourcedData<T>>(key);
        if (saved is not null && DateTimeOffset.UtcNow - saved.RetrievedAt < ttl) return saved;
        try
        {
            using var response = await http.GetAsync("api/data/" + path);
            if (!response.IsSuccessStatusCode)
            {
                if (saved is not null) return saved with { IsStale = true };
                var error = await response.Content.ReadFromJsonAsync<ApiError>();
                throw new DomainException(error?.Message ?? "Nie udało się pobrać danych. Spróbuj ponownie.");
            }
            var result = await response.Content.ReadFromJsonAsync<SourcedData<T>>() ?? throw new JsonException();
            await _cacheGate.WaitAsync();
            try
            {
                var index = await store.GetAsync<List<string>>("travelio.data.index") ?? [];
                index.Remove(key); index.Add(key);
                while (index.Count > 80) { await store.RemoveAsync(index[0]); index.RemoveAt(0); }
                await store.SetAsync(key, result);
                await store.SetAsync("travelio.data.index", index);
            }
            finally { _cacheGate.Release(); }
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (saved is not null) return saved with { IsStale = true };
            throw new DomainException("Serwer lub dostawca danych nie odpowiada. Zapisane plany są nadal dostępne. Spróbuj ponownie za chwilę.");
        }
    }
}
