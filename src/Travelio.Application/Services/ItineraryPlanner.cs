using Travelio.Domain;

namespace Travelio.Application.Services;

/// <summary>Time-budget planner. Pace affects the length of the day and breaks, never the time required for a visit.</summary>
public sealed class ItineraryPlanner : IItineraryPlanner
{
    public static PacePolicy Policy(TravelPace pace) => pace switch
    {
        TravelPace.Relaxed => new(new(10, 0), new(17, 0), 270, 25, 75),
        TravelPace.Balanced => new(new(9, 0), new(18, 0), 390, 15, 60),
        TravelPace.Intensive => new(new(8, 30), new(20, 0), 540, 10, 45),
        _ => throw new DomainException("Nieprawidłowe tempo podróży.")
    };

    public IReadOnlyList<PlanDay> Generate(Destination destination, int days, TravelPace pace, int travelers)
    {
        if (days is < 1 or > 30 || travelers is < 1 or > 20 || !Enum.IsDefined(pace))
            throw new DomainException("Wyjazd może trwać 1–30 dni i obejmować 1–20 osób.");

        var policy = Policy(pace);
        var unvisited = destination.Attractions.DistinctBy(x => x.Id).ToList();
        var result = new List<PlanDay>(days);
        for (var day = 1; day <= days; day++)
        {
            var plan = new PlanDay { Number = day };
            var position = destination.Location;
            var minute = (int)policy.StartsAt.ToTimeSpan().TotalMinutes;
            var active = 0;
            var lunchTaken = false;
            // 20 is the storage/routing safety limit, not a sightseeing target.
            while (plan.Stops.Count < 20 && unvisited.Count > 0)
            {
                var candidates = unvisited.Select(attraction =>
                {
                    // Walking estimate includes a street-detour factor; the map provides measured routing.
                    var travel = Math.Max(5, (int)Math.Ceiling(GeoDistance.Kilometers(position, attraction.Location) * 1.3 / 4.5 * 60));
                    var duration = attraction.VisitDuration?.Minutes ?? attraction.DurationMinutes;
                    var start = Math.Max(minute + travel + (plan.Stops.Count > 0 ? policy.BreakMinutes : 0),
                        (int)attraction.OpensAt.ToTimeSpan().TotalMinutes);
                    var lunch = !lunchTaken && start + duration > 13 * 60;
                    if (lunch) start += policy.LunchMinutes;
                    return new { attraction, travel, start, end = start + duration, duration, lunch };
                }).Where(x => x.duration > 0 && active + x.duration + x.travel <= policy.ActiveMinutes &&
                    x.end <= Math.Min(policy.EndsAt.ToTimeSpan().TotalMinutes, x.attraction.ClosesAt.ToTimeSpan().TotalMinutes))
                  .OrderBy(x => x.travel).ThenBy(x => x.start).ToList();

                if (candidates.Count == 0) break;
                var chosen = candidates[0];
                plan.Stops.Add(new PlanStop
                {
                    AttractionId = chosen.attraction.Id, Title = chosen.attraction.Name,
                    OriginalTitle = chosen.attraction.OriginalName,
                    LocalizedNames = chosen.attraction.LocalizedNames is null ? null : new(chosen.attraction.LocalizedNames),
                    StartsAt = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(chosen.start)),
                    DurationMinutes = chosen.duration, VisitDuration = chosen.attraction.VisitDuration,
                    TravelMinutes = chosen.travel, EstimatedCostPln = chosen.attraction.CostPln * travelers,
                    SourceUrl = chosen.attraction.SourceUrl, OpeningHours = chosen.attraction.OpeningHours,
                    Latitude = chosen.attraction.Location.Latitude, Longitude = chosen.attraction.Location.Longitude
                });
                unvisited.Remove(chosen.attraction);
                position = chosen.attraction.Location;
                active += chosen.duration + chosen.travel;
                minute = chosen.end;
                lunchTaken |= chosen.lunch;
            }
            result.Add(plan);
        }
        return result;
    }
}

public sealed record PacePolicy(TimeOnly StartsAt, TimeOnly EndsAt, int ActiveMinutes, int BreakMinutes, int LunchMinutes);

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

