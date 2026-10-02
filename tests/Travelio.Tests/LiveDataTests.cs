using System.Net;
using System.Text.Json;
using Microsoft.JSInterop;
using Travelio.Application;
using Travelio.Domain;
using Travelio.Infrastructure;
using Travelio.UI.Services;

namespace Travelio.Tests;

public sealed class LiveDataTests
{
    [Fact]
    public async Task Missing_indexeddb_value_can_be_read_as_a_boolean_without_crashing_the_layout()
    {
        var store = new BrowserLocalStore(new JsonRuntime("null"));
        Assert.False(await store.GetAsync<bool>("missing"));
        Assert.Null(await store.GetAsync<Workspace>("missing"));
        Assert.True(await new BrowserLocalStore(new JsonRuntime("true")).GetAsync<bool>("enabled"));
    }
    [Fact]
    public void Osm_places_keep_real_sources_and_do_not_invent_prices_or_reviews()
    {
        using var json = JsonDocument.Parse("""
        {"elements":[
         {"type":"node","id":1,"lat":50.06,"lon":19.94,"tags":{"name":"Muzeum","tourism":"museum","opening_hours":"Tu-Su 10:00-18:00"}},
         {"type":"way","id":2,"center":{"lat":50.061,"lon":19.941},"tags":{"name":"Hotel Real","tourism":"hotel","website":"javascript:alert(1)"}},
         {"type":"node","id":3,"lat":50.06,"lon":19.94,"tags":{"name":"Taras","tourism":"viewpoint","fee":"no"}},
         {"type":"node","id":4,"lat":50.0601,"lon":19.9401,"tags":{"name":"Muzeum","tourism":"museum"}},
         {"type":"node","id":5,"lat":51,"lon":20,"tags":{"name":"Daleko","tourism":"museum"}}
        ]}
        """);
        var places = OpenTravelDataProvider.ParsePlaces(json.RootElement, new(50.06, 19.94));
        Assert.Equal(2, places.Attractions.Length);
        var museum = Assert.Single(places.Attractions, x => x.Name == "Muzeum");
        Assert.Null(museum.CostPln); Assert.Equal("Tu-Su 10:00-18:00", museum.OpeningHours);
        Assert.Equal("https://www.openstreetmap.org/node/1", museum.SourceUrl);
        Assert.Equal(0, places.Attractions.Single(x => x.Name == "Taras").CostPln);
        Assert.Null(Assert.Single(places.Accommodations).Website);
    }
    [Fact]
    public void Polyline6_uses_the_routing_providers_coordinate_precision()
    {
        var decoded = OpenTravelDataProvider.DecodePolyline("_izlhA~rlgdF_{geC~ywl@_kwzCn`{nI");
        Assert.Equal(3, decoded.Length);
        Assert.Equal(38.5, decoded[0].Latitude, 5); Assert.Equal(-120.2, decoded[0].Longitude, 5);
        Assert.Throws<JsonException>(() => OpenTravelDataProvider.DecodePolyline("~"));
    }
    [Fact]
    public void Booking_link_encodes_a_real_hotel_and_the_travel_dates_without_affiliate_parameters()
    {
        var trip = PlannerTests.ValidTrip(); trip.StartDate = new(2027, 4, 10); trip.Days = 5;
        var link = TravelLinks.Booking(new DemoDestinationCatalog().Get("lisbon"), trip, "Hotel A & B");
        Assert.StartsWith("https://www.booking.com/searchresults.pl.html?", link);
        Assert.Contains("Hotel%20A%20%26%20B", link); Assert.Contains("checkin=2027-04-10", link);
        Assert.Contains("checkout=2027-04-14", link);
        trip.Days = 1;
        Assert.Contains("checkout=2027-04-11", TravelLinks.Booking(new DemoDestinationCatalog().Get("lisbon"), trip));
    }
    private sealed class JsonRuntime(string json) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(json)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
