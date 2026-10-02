using Travelio.Domain;

namespace Travelio.Application.Services;

/// <summary>Deterministic nearest-neighbour planner with opening hours, travel buffers and a lunch break.</summary>
public sealed class ItineraryPlanner : IItineraryPlanner
{
    public IReadOnlyList<PlanDay> Generate(Destination destination, int days, TravelPace pace, int travelers)
    {
        if (days is < 1 or > 30 || travelers is < 1 or > 20 || !Enum.IsDefined(pace))
            throw new DomainException("Wyjazd może trwać 1–30 dni i obejmować 1–20 osób.");

        var unvisited = destination.Attractions.ToList();
        var result = new List<PlanDay>(days);
        var dailyLimit = pace switch { TravelPace.Relaxed => 3, TravelPace.Intensive => 7, _ => 5 };
        unvisited = unvisited.Take(Math.Max(30, days * dailyLimit + 10)).ToList();
        var endOfDay = pace == TravelPace.Intensive ? 20 * 60 : 18 * 60;
        var durationFactor = pace switch { TravelPace.Relaxed => 1.2, TravelPace.Intensive => .75, _ => 1.0 };
        for (var day = 1; day <= days; day++)
        {
            var plan = new PlanDay { Number = day };
            var position = destination.Location;
            var minute = 9 * 60;
            var lunchTaken = false;
            while (plan.Stops.Count < dailyLimit && unvisited.Count > 0)
            {
                if (!lunchTaken && minute >= 12 * 60) { minute += 60; lunchTaken = true; }
                var candidates = unvisited.Select(attraction =>
                {
                    var travel = Math.Max(10, (int)Math.Ceiling(GeoDistance.Kilometers(position, attraction.Location) / 4.5 * 60) + 10);
                    var start = Math.Max(minute + travel, attraction.OpensAt.Hour * 60 + attraction.OpensAt.Minute);
                    var duration = Math.Max(20, (int)Math.Ceiling(attraction.DurationMinutes * durationFactor));
                    var end = start + duration;
                    return new { attraction, travel, start, end, duration };
                }).Where(x => x.end <= Math.Min(endOfDay, x.attraction.ClosesAt.Hour * 60 + x.attraction.ClosesAt.Minute))
                  .OrderBy(x => x.travel).ThenBy(x => x.start).ToList();

                if (candidates.Count == 0) break;
                var chosen = candidates[0];
                plan.Stops.Add(new PlanStop
                {
                    AttractionId = chosen.attraction.Id,
                    Title = chosen.attraction.Name,
                    StartsAt = new TimeOnly(chosen.start / 60, chosen.start % 60),
                    DurationMinutes = chosen.duration,
                    TravelMinutes = chosen.travel,
                    EstimatedCostPln = chosen.attraction.CostPln * travelers,
                    SourceUrl = chosen.attraction.SourceUrl,
                    OpeningHours = chosen.attraction.OpeningHours,
                    Latitude = chosen.attraction.Location.Latitude,
                    Longitude = chosen.attraction.Location.Longitude
                });
                unvisited.Remove(chosen.attraction);
                position = chosen.attraction.Location;
                minute = chosen.end;
            }
            result.Add(plan);
        }
        return result;
    }
}

public static class GeoDistance
{
    public static double Kilometers(Coordinate from, Coordinate to)
    {
        const double radians = Math.PI / 180;
        var deltaLat = (to.Latitude - from.Latitude) * radians;
        var deltaLon = (to.Longitude - from.Longitude) * radians;
        var a = Math.Pow(Math.Sin(deltaLat / 2), 2)
            + Math.Cos(from.Latitude * radians) * Math.Cos(to.Latitude * radians) * Math.Pow(Math.Sin(deltaLon / 2), 2);
        return 6371 * 2 * Math.Atan2(Math.Sqrt(Math.Clamp(a, 0, 1)), Math.Sqrt(Math.Max(0, 1 - a)));
    }
}

