using Travelio.Domain;

namespace Travelio.Application;

public sealed class DemoTravelOfferProvider : ITravelOfferProvider
{
    public Task<IReadOnlyList<TravelOffer>> SearchAsync(Destination destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TravelOffer> offers =
        [
            new("stay-boutique", $"{destination.Name} · Boutique Stay", "Nocleg", "Przykładowy hotel · pokój dla 2 osób · cena za noc",
                destination.DailyBudgetPln * 0.9m, 4.8m, 124,
                [new("Anna · przykład", 5, "Przykładowa opinia: przyjemny pokój i dobra lokalizacja."),new("Marek · przykład",4.5m,"Przykładowa opinia: pomocna obsługa i smaczne śniadanie.")]),
            new("stay-studio", "City Garden Apartments", "Nocleg", "Przykładowy apartament · aneks kuchenny · cena za noc",
                destination.DailyBudgetPln * 0.7m, 4.6m, 86,
                [new("Kasia · przykład",4.5m,"Przykładowa opinia: spokojne miejsce na kilkudniowy pobyt.")]),
            new("flight", $"Warszawa → {destination.Name}", "Lot", "Przykładowe połączenie · w obie strony · 1 osoba · bez bagażu",
                destination.Region == "Europa" ? 890 : 3250, 4.4m, 218,
                [new("Tomek · przykład",4,"Przykładowa opinia: wygodny przelot i sprawna obsługa.")])
        ];
        return Task.FromResult(offers);
    }
}

public sealed class UnconfiguredRegionalAlertProvider : IRegionalAlertProvider
{
    public Task<IReadOnlyList<RegionalAlert>> GetAlertsAsync(Destination destination, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RegionalAlert>>(
        [
            new("Sprawdź komunikaty przed wyjazdem", $"Aktualne alerty dla kraju {destination.Country} nie zostały pobrane. Brak danych nie oznacza braku zagrożeń.",
                "unknown", "https://www.gov.pl/web/dyplomacja/informacje-dla-podrozujacych", null, false)
        ]);
}

