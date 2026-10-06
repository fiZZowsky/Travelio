using Travelio.Domain;

namespace Travelio.Application.Services;

/// <summary>Refreshes source labels while preserving personal titles, timings and visit status.</summary>
public static class PlaceNameUpdater
{
    public static int Apply(Trip trip, IEnumerable<Attraction> places)
    {
        var byId = places.DistinctBy(p => p.Id).ToDictionary(p => p.Id);
        var updated = 0;
        foreach (var stop in trip.Itinerary.SelectMany(d => d.Stops))
        {
            if (!byId.TryGetValue(stop.AttractionId, out var place) || place.LocalizedNames?.Count is not > 0) continue;
            // Legacy plans have no OriginalTitle. Only recognize the old title as generated if it
            // matches a source label; a user's custom title must never be overwritten.
            var generated = stop.Title == stop.OriginalTitle || stop.Title == place.OriginalName ||
                stop.Title == place.Name || stop.LocalizedNames?.Values.Contains(stop.Title) == true ||
                place.LocalizedNames.Values.Contains(stop.Title);
            if (!generated) continue;
            stop.LocalizedNames = new(place.LocalizedNames);
            stop.OriginalTitle = place.OriginalName ?? place.Name;
            // Keep the stored title recognizable to renderers even when a source renamed a place.
            stop.Title = stop.OriginalTitle;
            updated++;
        }
        return updated;
    }
}
