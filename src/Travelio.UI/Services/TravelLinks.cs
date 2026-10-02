using System.Globalization;
using Travelio.Domain;

namespace Travelio.UI.Services;

public static class TravelLinks
{
    public static string GoogleMaps(string name, Coordinate location) => "https://www.google.com/maps/search/?api=1&query=" +
        Uri.EscapeDataString($"{name} {location.Latitude.ToString(CultureInfo.InvariantCulture)},{location.Longitude.ToString(CultureInfo.InvariantCulture)}");
    public static string Booking(Destination destination, Trip trip, string? hotel = null) =>
        "https://www.booking.com/searchresults.pl.html?ss=" + Uri.EscapeDataString($"{hotel} {destination.Name} {destination.Country}".Trim()) +
        $"&checkin={trip.StartDate:yyyy-MM-dd}&checkout={(trip.Days > 1 ? trip.EndDate : trip.StartDate.AddDays(1)):yyyy-MM-dd}&group_adults={trip.Travelers}&no_rooms=1&group_children=0";
}
