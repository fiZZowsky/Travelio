using Travelio.Application;
using Travelio.Application.Services;
using Travelio.Domain;
namespace Travelio.Tests;

public sealed class PlannerTests
{
    private readonly DemoDestinationCatalog _catalog = new();
    [Theory]
    [InlineData(TravelPace.Relaxed)]
    [InlineData(TravelPace.Balanced)]
    [InlineData(TravelPace.Intensive)]
    public void Plans_respect_hours_and_never_repeat_attractions(TravelPace pace)
    {
        foreach (var destination in _catalog.All)
        {
            var plan = new ItineraryPlanner().Generate(destination, 7, pace, 2);
            Assert.Equal(7, plan.Count);
            var stops = plan.SelectMany(x => x.Stops).ToArray();
            Assert.NotEmpty(stops);
            Assert.Equal(stops.Length, stops.Select(x => x.AttractionId).Distinct().Count());
            Assert.All(plan, day =>
            {
                var policy = ItineraryPlanner.Policy(pace);
                Assert.True(day.Stops.Sum(x => x.DurationMinutes + x.TravelMinutes) <= policy.ActiveMinutes);
                var previousEnd = policy.StartsAt;
                foreach (var stop in day.Stops)
                {
                    var attraction = destination.Attractions.Single(x => x.Id == stop.AttractionId);
                    Assert.True(stop.StartsAt >= previousEnd);
                    Assert.True(stop.StartsAt >= attraction.OpensAt);
                    var ends = stop.StartsAt.AddMinutes(stop.DurationMinutes);
                    Assert.True(ends <= attraction.ClosesAt);
                    Assert.True(ends <= policy.EndsAt);
                    Assert.Equal(attraction.CostPln * 2, stop.EstimatedCostPln);
                    previousEnd = ends;
                }
            });
        }
    }
    [Fact]
    public void Pace_changes_time_budget_without_shortening_visits_or_capping_at_seven()
    {
        var places = Enumerable.Range(1, 80).Select(n => new Attraction(n.ToString(), $"Miejsce {n}", "", TravelStyle.Culture,
            new(50.06 + n * .00005, 19.94), 40, null, new(9, 0), new(20, 0))).ToArray();
        var destination = _catalog.Get("rome") with { Location = new(50.06, 19.94), Attractions = places };
        var planner = new ItineraryPlanner();
        var relaxed = planner.Generate(destination, 5, TravelPace.Relaxed, 2);
        var balanced = planner.Generate(destination, 5, TravelPace.Balanced, 2);
        var intensive = planner.Generate(destination, 5, TravelPace.Intensive, 2);
        Assert.True(relaxed[0].Stops.Count < balanced[0].Stops.Count);
        Assert.True(balanced[0].Stops.Count < intensive[0].Stops.Count);
        Assert.True(intensive[0].Stops.Count > 7);
        Assert.All(relaxed.Concat(balanced).Concat(intensive).SelectMany(x => x.Stops), stop => Assert.Equal(40, stop.DurationMinutes));
        Assert.All(intensive.SelectMany(x => x.Stops), x => Assert.Null(x.EstimatedCostPln));
    }
    [Fact]
    public void Long_museum_visits_leave_less_room_than_short_viewpoints()
    {
        Destination City(int duration) => _catalog.Get("rome") with
        {
            Attractions = Enumerable.Range(1, 30).Select(n => new Attraction(n.ToString(), "Test", "", TravelStyle.Culture,
                _catalog.Get("rome").Location, duration, null, new(8, 0), new(20, 0))).ToArray()
        };
        var planner = new ItineraryPlanner();
        var museums = planner.Generate(City(180), 1, TravelPace.Intensive, 1)[0];
        var viewpoints = planner.Generate(City(25), 1, TravelPace.Intensive, 1)[0];
        Assert.InRange(museums.Stops.Count, 1, 3);
        Assert.True(viewpoints.Stops.Count > 7);
        Assert.All(museums.Stops, stop => Assert.Equal(180, stop.DurationMinutes));
    }
    [Fact]
    public void Rejects_invalid_lengths_instead_of_allocating_unbounded_plans()
    {
        Assert.Throws<DomainException>(() => new ItineraryPlanner().Generate(_catalog.Get("rome"), 100000, TravelPace.Balanced, 2));
        Assert.Throws<DomainException>(() => new ItineraryPlanner().Generate(_catalog.Get("rome"), 5, TravelPace.Balanced, 0));
    }
    [Fact]
    public void Preference_match_is_deterministic_and_explained()
    {
        var service = new RecommendationService(_catalog);
        var preferences = new TravelerPreferences(300, 7, TravelPace.Relaxed, TravelStyle.Beach, 6, "Azja");
        var first = service.Recommend(preferences);
        Assert.Equal("bali", first[0].Destination.Id);
        Assert.Equal(first.Select(x => x.Match), service.Recommend(preferences).Select(x => x.Match));
        Assert.All(first, x => Assert.InRange(x.Match, 0, 100));
        Assert.NotEmpty(first[0].Reasons);
    }
    [Fact]
    public void Currency_conversion_rounds_each_transaction_and_retains_its_original_rate()
    {
        var trip = new Trip { Days = 5, Travelers = 2, Budget = 100 };
        var expense = new Expense { Description = "Kawa", Currency = "EUR", Amount = 10.01m, RateToPln = 4.345m };
        trip.AddExpense(expense);
        trip.AddExpense(expense);
        var result = new BudgetService().Analyze(trip);
        Assert.Single(trip.Expenses);
        Assert.Equal(43.49m, result.Spent);
        Assert.Equal(56.51m, result.Remaining);
        Assert.Equal(21.745m, result.PerPerson);
    }
    [Fact]
    public void Reminders_use_destination_timezone_and_cancel_completed_stops()
    {
        var stop = new PlanStop { Title = "Poranek", StartsAt = new(9, 0), DurationMinutes = 60 };
        var trip = new Trip { StartDate = new(2026, 7, 1), Days = 1, Itinerary = [new() { Number = 1, Stops = [stop] }] };
        var monitor = new ScheduleMonitor();
        var now = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
        var reminder = Assert.Single(monitor.GetReminders(trip, _catalog.Get("lisbon"), now));
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 8, 15, 0, TimeSpan.Zero), reminder.DueAt);
        stop.Completed = true;
        Assert.Empty(monitor.GetReminders(trip, _catalog.Get("lisbon"), now));
    }
    [Fact]
    public void Validation_rejects_null_collections_and_nonfinite_coordinates()
    {
        var trip = ValidTrip();
        trip.Expenses = null!;
        Assert.Throws<DomainException>(() => TripValidator.Validate(trip, _catalog));
        trip = ValidTrip();
        trip.Itinerary = [new() { Number = 1, Stops = [new() { Title = "Test", DurationMinutes = 60, Latitude = double.NaN }] }];
        Assert.Throws<DomainException>(() => TripValidator.Validate(trip, _catalog));
    }
    [Fact]
    public void Great_circle_distance_is_symmetric_and_finite_at_antipodes()
    {
        var first = new Coordinate(0, 0); var second = new Coordinate(0, 180);
        var distance = GeoDistance.Kilometers(first, second);
        Assert.InRange(distance, 20000, 20020);
        Assert.Equal(distance, GeoDistance.Kilometers(second, first));
    }
    internal static Trip ValidTrip() => new() { Name = "Testowa Lizbona", DestinationId = "lisbon", Days = 5, Budget = 4000, Travelers = 2 };
}

