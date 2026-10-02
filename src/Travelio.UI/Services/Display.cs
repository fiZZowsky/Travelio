using System.Globalization;
using Travelio.Domain;

namespace Travelio.UI.Services;

public static class Display
{
    public static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");
    public static string Money(decimal value) => value.ToString(value == decimal.Truncate(value) ? "N0" : "N2", Polish) + " zł";
    public static string Date(DateOnly date) => date.ToString("d MMM", Polish);
    public static string Category(ExpenseCategory category) => category switch
    {
        ExpenseCategory.Transport => "Transport", ExpenseCategory.Accommodation => "Noclegi",
        ExpenseCategory.Food => "Jedzenie", ExpenseCategory.Attractions => "Atrakcje",
        ExpenseCategory.Shopping => "Zakupy", _ => "Inne"
    };
    public static string Style(TravelStyle style) => style switch
    {
        TravelStyle.Culture => "Kultura i historia", TravelStyle.Nature => "Blisko natury",
        TravelStyle.Beach => "Plaża i odpoczynek", TravelStyle.Food => "Lokalne smaki", _ => "Przygoda"
    };
    public static string Pace(TravelPace pace) => pace switch
    { TravelPace.Relaxed => "Spokojnie", TravelPace.Intensive => "Intensywnie", _ => "W swoim rytmie" };
    public static string Image(Destination destination) => $"_content/Travelio.UI/images/{destination.Image}";
}

