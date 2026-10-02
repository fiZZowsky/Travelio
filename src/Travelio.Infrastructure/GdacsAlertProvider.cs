using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Travelio.Application;
using Travelio.Application.Services;
using Travelio.Domain;

namespace Travelio.Infrastructure;

/// <summary>Read-only GDACS RSS adapter. Country and approximate radius matching is informational, not a safety guarantee.</summary>
public sealed class GdacsAlertProvider(IHttpClientFactory clients, IMemoryCache cache, ILogger<GdacsAlertProvider> logger) : IRegionalAlertProvider
{
    private static readonly SemaphoreSlim FeedLock = new(1, 1);
    private const string FeedKey = "gdacs-feed-v1";
    public async Task<IReadOnlyList<RegionalAlert>> GetAlertsAsync(Destination destination, CancellationToken cancellationToken = default)
    {
        try
        {
            var feed = await GetFeedAsync(cancellationToken);
            return SelectAlerts(feed.Document, destination, feed.CheckedAt);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException or IOException)
        {
            logger.LogWarning("GDACS feed unavailable: {ErrorType}", ex.GetType().Name);
            return [new("Dane o zagrożeniach są niedostępne", "Nie udało się pobrać aktualnego kanału GDACS. Sprawdź komunikaty MSZ oraz lokalne służby. Brak danych nie oznacza braku zagrożeń.",
                "unknown", "https://www.gdacs.org/", null, false)];
        }
    }

    private async Task<Feed> GetFeedAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<Feed>(FeedKey, out var cached) && cached is not null) return cached;
        await FeedLock.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue<Feed>(FeedKey, out cached) && cached is not null) return cached;
            using var client = clients.CreateClient("gdacs");
            using var response = await client.GetAsync("https://www.gdacs.org/xml/rss.xml", HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 5_000_000) throw new IOException("Feed is too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 5_000_000 });
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, ct);
            if (document.Root?.Name != "rss" || document.Root.Element("channel") is null) throw new XmlException("Invalid feed.");
            var feed = new Feed(document, DateTimeOffset.UtcNow);
            cache.Set(FeedKey, feed, TimeSpan.FromMinutes(15));
            return feed;
        }
        finally { FeedLock.Release(); }
    }

    public static IReadOnlyList<RegionalAlert> SelectAlerts(XDocument document, Destination destination, DateTimeOffset checkedAt)
    {
        XNamespace gdacs = "http://www.gdacs.org";
        XNamespace georss = "http://www.georss.org/georss";
        var region = new RegionInfo(destination.CountryCode);
        var alerts = new List<RegionalAlert>();
        foreach (var item in document.Descendants("item"))
        {
            if (!string.Equals((string?)item.Element(gdacs + "iscurrent"), "true", StringComparison.OrdinalIgnoreCase)) continue;
            var sameCountry = (string?)item.Element(gdacs + "iso3") == region.ThreeLetterISORegionName ||
                ((string?)item.Element(gdacs + "country") ?? "").Split(',').Any(x => x.Trim().Equals(region.EnglishName, StringComparison.OrdinalIgnoreCase));
            var coordinates = ((string?)item.Element(georss + "point") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var nearby = coordinates.Length == 2 && double.TryParse(coordinates[0], CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(coordinates[1], CultureInfo.InvariantCulture, out var lon) &&
                double.IsFinite(lat) && double.IsFinite(lon) && lat is >= -90 and <= 90 && lon is >= -180 and <= 180 &&
                GeoDistance.Kilometers(destination.Location, new(lat, lon)) <= 500;
            if (!sameCountry && !nearby) continue;
            var source = (string?)item.Element("link");
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                (uri.Host != "www.gdacs.org" && uri.Host != "gdacs.org")) continue;
            var severity = ((string?)item.Element(gdacs + "alertlevel"))?.ToLowerInvariant() ?? "unknown";
            var kind = (string?)item.Element(gdacs + "eventtype") switch
            {
                "TC" => "Cyklon tropikalny", "EQ" => "Trzęsienie ziemi", "FL" => "Powódź",
                "VO" => "Aktywność wulkaniczna", "DR" => "Susza", "WF" => "Pożar", _ => "Zdarzenie regionalne"
            };
            alerts.Add(new(kind + " · GDACS", $"Zdarzenie {(sameCountry ? "w kraju podróży" : "do 500 km od kierunku")}. " +
                $"Poziom GDACS: {severity}. Szczegóły i zasięg sprawdź w źródle; komunikat nie jest prognozą na termin Twojego wyjazdu.",
                severity, uri.AbsoluteUri, checkedAt, true));
        }
        if (alerts.Count == 0)
            alerts.Add(new("Brak pasujących zdarzeń w kanale GDACS", "W ostatnio pobranym kanale nie znaleziono bieżących zdarzeń w kraju lub do 500 km od kierunku. Kanał nie obejmuje wszystkich zagrożeń. Sprawdź również MSZ i lokalne służby.",
                "info", "https://www.gdacs.org/", checkedAt, true));
        return alerts.OrderByDescending(x => x.Severity == "red" ? 3 : x.Severity == "orange" ? 2 : 1).Take(10).ToArray();
    }
    private sealed record Feed(XDocument Document, DateTimeOffset CheckedAt);
}

