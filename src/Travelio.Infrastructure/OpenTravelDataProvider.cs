using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Travelio.Application;
using Travelio.Application.Services;
using Travelio.Domain;

namespace Travelio.Infrastructure;

/// <summary>Public data adapters. Provider URLs are configuration, never supplied by a client request.</summary>
public sealed class OpenTravelDataProvider(IHttpClientFactory factory, IMemoryCache cache,
    IConfiguration configuration, IDestinationCatalog catalog, ILogger<OpenTravelDataProvider> logger) : ITravelDataProvider
{
    private readonly SemaphoreSlim _requests = new(3);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private string Endpoint(string key, string fallback) => configuration[$"Travelio:Providers:{key}"] ?? fallback;
    private static string N(double n) => n.ToString("0.#####", Invariant);
    private static string Text(JsonElement e, string key) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static string Cut(string text, int length = 200) => text[..Math.Min(text.Length, length)];
    private static string Query(string query)
    {
        query = query?.Trim() ?? "";
        if (query.Length is < 2 or > 120) throw new DomainException("Wpisz od 2 do 120 znaków.");
        return Uri.EscapeDataString(query);
    }
    private static void Check(Coordinate p)
    { if (!TripValidator.ValidCoordinate(p)) throw new DomainException("Nieprawidłowa lokalizacja."); }

    private async Task<T> Cached<T>(string key, TimeSpan ttl, Func<Task<T>> fetch, CancellationToken ct)
    {
        if (cache.TryGetValue<T>(key, out var value)) return value!;
        await _requests.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue<T>(key, out value)) return value!;
            value = await fetch();
            cache.Set(key, value, ttl);
            return value;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning("Travel data provider failed: {Type}: {Message}", ex.GetType().Name, ex.Message);
            throw new TravelDataUnavailableException("Źródło danych jest chwilowo niedostępne. Spróbuj ponownie; zapisane miejsca i plany nadal są dostępne.");
        }
        finally { _requests.Release(); }
    }
    private async Task<JsonDocument> Read(string url, CancellationToken ct)
    {
        using var response = await factory.CreateClient("travel-data").GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
    }
    public Task<SourcedData<Destination[]>> SearchCitiesAsync(string query, CancellationToken ct = default)
    {
        var q = Query(query);
        return Cached("cities:" + q, TimeSpan.FromDays(7), async () =>
        {
            using var json = await Read($"{Endpoint("Geocoding", "https://geocoding-api.open-meteo.com/v1/")}search?name={q}&count=12&language=pl", ct);
            var cities = json.RootElement.TryGetProperty("results", out var rows)
                ? rows.EnumerateArray().Where(x => Text(x, "country_code").Length == 2 && Text(x, "timezone").Length > 0).Select(City).ToArray() : [];
            return new SourcedData<Destination[]>(cities, "Open-Meteo / GeoNames", "https://open-meteo.com/en/docs/geocoding-api", DateTimeOffset.UtcNow);
        }, ct);
    }
    public async Task<Destination> GetCityAsync(string id, CancellationToken ct = default)
    {
        if (!id.StartsWith("geo-", StringComparison.Ordinal)) return catalog.Get(id);
        if (!int.TryParse(id.AsSpan(4), out var number) || number <= 0) throw new DomainException("Nieprawidłowy kierunek.");
        return await Cached("city:" + id, TimeSpan.FromDays(7), async () =>
        {
            using var json = await Read($"{Endpoint("Geocoding", "https://geocoding-api.open-meteo.com/v1/")}get?id={number}&language=pl", ct);
            return City(json.RootElement);
        }, ct);
    }
    private static Destination City(JsonElement e)
    {
        var timezone = Text(e, "timezone");
        var region = timezone.Split('/')[0] switch { "Europe" => "Europa", "Asia" => "Azja", "Africa" => "Afryka", "America" => "Ameryka", "Australia" or "Pacific" => "Oceania", _ => "Świat" };
        return new($"geo-{e.GetProperty("id").GetInt32()}", Cut(Text(e, "name")), Text(e, "country"), Text(e, "country_code"),
            region, string.Join(" · ", new[] { Text(e, "admin1"), Text(e, "country") }.Where(x => x.Length > 0)), timezone, 0,
            new(e.GetProperty("latitude").GetDouble(), e.GetProperty("longitude").GetDouble()), "world.svg", [], [], [],
            "https://www.geonames.org/" + e.GetProperty("id").GetInt32(), DateTimeOffset.UtcNow);
    }
    public Task<SourcedData<PlaceCollection>> GetPlacesAsync(Coordinate center, CancellationToken ct = default)
    {
        Check(center);
        var lat = Math.Round(center.Latitude, 3); var lon = Math.Round(center.Longitude, 3);
        return Cached($"places:{N(lat)}:{N(lon)}", TimeSpan.FromHours(24), async () =>
        {
            var radius = .035;
            var dx = radius / Math.Max(.25, Math.Cos(lat * Math.PI / 180));
            var box = $"{N(Math.Max(-90, lat - radius))},{N(Math.Max(-180, lon - dx))},{N(Math.Min(90, lat + radius))},{N(Math.Min(180, lon + dx))}";
            // Named, user-facing categories keep the request small on shared Overpass instances.
            var query = $"[out:json][timeout:12][maxsize:67108864];(nwr[name][tourism~\"^(attraction|museum|viewpoint|gallery|zoo|theme_park|hotel|hostel|guest_house|apartment|motel)$\"]({box});nwr[name][historic~\"^(castle|monument)$\"]({box});nwr[name][leisure=park]({box}););out center 700;";
            using var json = await Read(Endpoint("Overpass", "https://overpass-api.de/api/interpreter") + "?data=" + Uri.EscapeDataString(query), ct);
            if (json.RootElement.TryGetProperty("remark", out _)) throw new HttpRequestException("Incomplete Overpass response");
            return new SourcedData<PlaceCollection>(ParsePlaces(json.RootElement, center), "© OpenStreetMap contributors · ODbL",
                "https://www.openstreetmap.org/copyright", DateTimeOffset.UtcNow);
        }, ct);
    }
    public static PlaceCollection ParsePlaces(JsonElement root, Coordinate center)
    {
        var attractions = new List<(Attraction Place, int Rank)>(); var hotels = new List<Accommodation>();
        foreach (var row in root.GetProperty("elements").EnumerateArray())
        {
            if (!row.TryGetProperty("tags", out var tags)) continue;
            var name = Text(tags, "name:pl"); if (name.Length == 0) name = Text(tags, "name");
            if (name.Length == 0) continue;
            var position = row.TryGetProperty("center", out var p) ? p : row;
            if (!position.TryGetProperty("lat", out var latitude) || !position.TryGetProperty("lon", out var longitude)) continue;
            var location = new Coordinate(latitude.GetDouble(), longitude.GetDouble());
            if (!TripValidator.ValidCoordinate(location) || GeoDistance.Kilometers(center, location) > 9) continue;
            var type = Text(row, "type"); if (type is not ("node" or "way" or "relation")) continue;
            var id = $"{type}/{row.GetProperty("id").GetInt64()}";
            var source = "https://www.openstreetmap.org/" + id;
            var tourism = Text(tags, "tourism");
            var address = string.Join(" ", new[] { Text(tags, "addr:street"), Text(tags, "addr:housenumber"), Text(tags, "addr:city") }.Where(x => x.Length > 0));
            if (tourism is "hotel" or "hostel" or "guest_house" or "apartment" or "motel")
            {
                hotels.Add(new(id, Cut(name), Cut(address), location, source, SafeWebsite(Text(tags, "website"))));
                continue;
            }
            var park = Text(tags, "leisure") == "park";
            var castle = Text(tags, "historic") == "castle";
            if (tourism is not ("attraction" or "museum" or "viewpoint" or "gallery" or "zoo" or "artwork" or "theme_park") && !park && !castle) continue;
            // Tiny artwork without encyclopedic context is a poor primary stop in an itinerary.
            if (tourism == "artwork" && Text(tags, "wikipedia").Length == 0 && Text(tags, "wikidata").Length == 0) continue;
            var kind = park ? "Park" : castle ? "Zamek" : tourism switch { "museum" => "Muzeum", "viewpoint" => "Punkt widokowy", "gallery" => "Galeria", "zoo" => "Ogród zoologiczny", "artwork" => "Sztuka w przestrzeni miasta", _ => "Atrakcja" };
            var duration = tourism is "museum" or "zoo" ? 90 : castle ? 75 : park ? 45 : 35;
            var description = Text(tags, "description:pl"); if (description.Length == 0) description = kind + (address.Length == 0 ? "" : " · " + address);
            var place = new Attraction(id, Cut(name), Cut(description, 500), park || tourism == "viewpoint" ? TravelStyle.Nature : TravelStyle.Culture,
                location, duration, Text(tags, "fee") == "no" ? 0 : null, new(9, 0), new(20, 0), source, Text(tags, "opening_hours"));
            var rank = (Text(tags, "wikipedia").Length > 0 ? 10 : 0) + (castle || tourism is "museum" or "attraction" ? 5 : 0);
            attractions.Add((place, rank));
        }
        var unique = new List<Attraction>();
        foreach (var row in attractions.OrderByDescending(x => x.Rank).ThenBy(x => GeoDistance.Kilometers(center, x.Place.Location)))
            if (!unique.Any(x => x.Name.Equals(row.Place.Name, StringComparison.OrdinalIgnoreCase) && GeoDistance.Kilometers(x.Location, row.Place.Location) < .3)) unique.Add(row.Place);
        return new(unique.Take(180).ToArray(), hotels.DistinctBy(x => x.Name.ToLowerInvariant()).OrderBy(x => GeoDistance.Kilometers(center, x.Location)).Take(50).ToArray());
    }
    private static string? SafeWebsite(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri.AbsoluteUri : null;
    public Task<SourcedData<Attraction[]>> SearchPlacesAsync(string query, Coordinate center, CancellationToken ct = default)
    {
        Check(center); var q = Query(query);
        return Cached($"search:{q}:{N(center.Latitude)}:{N(center.Longitude)}", TimeSpan.FromDays(7), async () =>
        {
            var url = Endpoint("Photon", "https://photon.komoot.io/api/") + $"?q={q}&lat={N(center.Latitude)}&lon={N(center.Longitude)}&limit=12";
            using var json = await Read(url, ct);
            var places = json.RootElement.GetProperty("features").EnumerateArray().Select(row =>
            {
                var p = row.GetProperty("properties"); var xy = row.GetProperty("geometry").GetProperty("coordinates");
                var type = Text(p, "osm_type") switch { "N" => "node", "W" => "way", _ => "relation" };
                var id = $"{type}/{p.GetProperty("osm_id").GetInt64()}";
                return new Attraction(id, Cut(Text(p, "name")), Cut(string.Join(" · ", new[] { Text(p, "street"), Text(p, "city"), Text(p, "country") }.Where(x => x.Length > 0))),
                    TravelStyle.Culture, new(xy[1].GetDouble(), xy[0].GetDouble()), 60, null, new(9, 0), new(20, 0), "https://www.openstreetmap.org/" + id);
            }).Where(x => x.Name.Length > 0 && GeoDistance.Kilometers(center, x.Location) < 100).ToArray();
            return new SourcedData<Attraction[]>(places, "Photon / © OpenStreetMap contributors", "https://www.openstreetmap.org/copyright", DateTimeOffset.UtcNow);
        }, ct);
    }
    public Task<SourcedData<WalkingRoute>> GetRouteAsync(Coordinate[] points, CancellationToken ct = default)
    {
        if (points.Length is < 2 or > 20) throw new DomainException("Trasa wymaga od 2 do 20 punktów.");
        foreach (var point in points) Check(point);
        if (points.Zip(points.Skip(1)).Sum(x => GeoDistance.Kilometers(x.First, x.Second)) > 100)
            throw new DomainException("Trasa piesza jest zbyt długa. Wybierz bliższe miejsca.");
        var request = JsonSerializer.Serialize(new { locations = points.Select(x => new { lat = x.Latitude, lon = x.Longitude }), costing = "pedestrian", units = "kilometers" });
        return Cached("route:" + request, TimeSpan.FromDays(7), async () =>
        {
            using var json = await Read(Endpoint("Routing", "https://valhalla1.openstreetmap.de/route") + "?json=" + Uri.EscapeDataString(request), ct);
            var trip = json.RootElement.GetProperty("trip"); var summary = trip.GetProperty("summary");
            var geometry = trip.GetProperty("legs").EnumerateArray().SelectMany(x => DecodePolyline(Text(x, "shape"))).ToArray();
            if (geometry.Length < 2) throw new JsonException("Empty route");
            return new SourcedData<WalkingRoute>(new(geometry, summary.GetProperty("length").GetDouble(), (int)Math.Ceiling(summary.GetProperty("time").GetDouble() / 60)),
                "Valhalla / OpenStreetMap", "https://www.openstreetmap.org/copyright", DateTimeOffset.UtcNow);
        }, ct);
    }
    public static Coordinate[] DecodePolyline(string encoded)
    {
        var result = new List<Coordinate>(); var index = 0; var lat = 0; var lon = 0;
        int Next()
        {
            var value = 0; var shift = 0; int b;
            do { if (index >= encoded.Length || shift > 30) throw new JsonException("Invalid polyline"); b = encoded[index++] - 63; if (b is < 0 or > 63) throw new JsonException("Invalid polyline"); value |= (b & 31) << shift; shift += 5; } while (b >= 32);
            return (value & 1) != 0 ? ~(value >> 1) : value >> 1;
        }
        while (index < encoded.Length) { lat += Next(); lon += Next(); result.Add(new(lat / 1e6, lon / 1e6)); }
        return result.ToArray();
    }
    public Task<SourcedData<ExchangeRate>> GetRateAsync(string currency, DateOnly date, CancellationToken ct = default)
    {
        currency = currency?.ToUpperInvariant() ?? "";
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') || date.Year < 2002 || date > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new DomainException("Wybierz walutę i datę od 2002 roku, nie późniejszą niż dziś.");
        return Cached($"rate:{currency}:{date:yyyy-MM-dd}", TimeSpan.FromHours(6), async () =>
        {
            if (currency == "PLN") return new SourcedData<ExchangeRate>(new(currency, 1, date), "NBP", "https://nbp.pl", DateTimeOffset.UtcNow);
            foreach (var table in new[] { "a", "b" })
            {
                var url = $"https://api.nbp.pl/api/exchangerates/rates/{table}/{currency}/{date.AddDays(-14):yyyy-MM-dd}/{date:yyyy-MM-dd}/?format=json";
                try
                {
                    using var json = await Read(url, ct);
                    var rate = json.RootElement.GetProperty("rates").EnumerateArray().Last();
                    return new SourcedData<ExchangeRate>(new(currency, rate.GetProperty("mid").GetDecimal(), DateOnly.Parse(Text(rate, "effectiveDate"), Invariant)), "Narodowy Bank Polski · kurs średni", "https://api.nbp.pl", DateTimeOffset.UtcNow);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
            }
            throw new TravelDataUnavailableException("NBP nie udostępnia kursu tej waluty dla wybranej daty. Podaj kurs z transakcji.");
        }, ct);
    }
}
