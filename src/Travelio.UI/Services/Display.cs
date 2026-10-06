using System.Globalization;
using Travelio.Domain;
using Travelio.Application.Localization;

namespace Travelio.UI.Services;

public static class Display
{
    public static CultureInfo Polish => L.Culture;
    public static string Money(decimal value) => value.ToString(value == decimal.Truncate(value) ? "N0" : "N2", L.Culture) + (L.Language == "en" ? " PLN" : L.T(" zł"));
    public static string PlaceName(Attraction place) => L.Name(place.Name, place.LocalizedNames);
    public static string StopName(PlanStop stop) => stop.LocalizedNames is null ||
        (stop.Title != stop.OriginalTitle && !stop.LocalizedNames.Values.Contains(stop.Title)) ? L.T(stop.Title) : L.Name(stop.Title, stop.LocalizedNames);
    public static string DurationBasis(VisitDuration? duration) => L.T(duration?.Basis switch
    {
        VisitDurationBasis.SourceRecommendation => L.T("Czas sugerowany przez obiekt"),
        VisitDurationBasis.UserEstimate => L.T("Twój czas wizyty"),
        _ => L.T("Szacowany czas wizyty")
    });
    public static string Date(DateOnly date) => date.ToString("d MMM", Polish);
    public static string Category(ExpenseCategory category) => category switch
    {
        ExpenseCategory.Transport => "Transport", ExpenseCategory.Accommodation => L.T("Noclegi"),
        ExpenseCategory.Food => L.T("Jedzenie"), ExpenseCategory.Attractions => L.T("Atrakcje"),
        ExpenseCategory.Shopping => L.T("Zakupy"), _ => L.T("Inne")
    };
    public static string Style(TravelStyle style) => style switch
    {
        TravelStyle.Culture => L.T("Kultura i historia"), TravelStyle.Nature => L.T("Blisko natury"),
        TravelStyle.Beach => L.T("Plaża i odpoczynek"), TravelStyle.Food => L.T("Lokalne smaki"), _ => L.T("Przygoda")
    };
    public static string Pace(TravelPace pace) => pace switch
    { TravelPace.Relaxed => L.T("Spokojnie"), TravelPace.Intensive => L.T("Intensywnie"), _ => L.T("W swoim rytmie") };
    public static string Image(Destination destination) => $"_content/Travelio.UI/images/{destination.Image}";
}

