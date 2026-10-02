using Travelio.Domain;

namespace Travelio.Application.Services;

public static class TripValidator
{
    public static void Validate(Trip trip, IDestinationCatalog catalog)
    {
        if (trip.DestinationId is null || trip.Notes is null || trip.Itinerary is null || trip.Expenses is null || trip.PackingList is null ||
            trip.Itinerary.Any(x => x is null || x.Stops is null || x.Stops.Any(s => s is null)) ||
            trip.Expenses.Any(x => x is null || x.Currency is null || x.PaidBy is null) ||
            trip.PackingList.Any(x => x is null || x.Category is null))
            throw new DomainException("Brakuje wymaganych danych podróży.");
        if (trip.Id == Guid.Empty) throw new DomainException("Brak identyfikatora podróży.");
        if (string.IsNullOrWhiteSpace(trip.Name) || trip.Name.Length > 100)
            throw new DomainException("Nazwa podróży musi mieć od 1 do 100 znaków.");
        var destination = catalog.Resolve(trip);
        ValidateDestination(destination);
        if (destination.Id != trip.DestinationId) throw new DomainException("Kierunek nie pasuje do podróży.");
        if (trip.Days is < 1 or > 30 || trip.Travelers is < 1 or > 20 || !Enum.IsDefined(trip.Pace))
            throw new DomainException("Sprawdź liczbę dni (1–30), osób (1–20) i tempo podróży.");
        if (trip.StartDate.Year is < 2000 or > 2100)
            throw new DomainException("Data wyjazdu musi być z lat 2000–2100.");
        if (trip.Budget is < 0 or > 100_000_000 || trip.Notes.Length > 10000)
            throw new DomainException("Budżet lub notatka przekracza dozwolony limit.");
        if (trip.Itinerary.Count > trip.Days || trip.Expenses.Count > 2000 || trip.PackingList.Count > 300)
            throw new DomainException("Przekroczono limit elementów podróży.");
        if (trip.Itinerary.Select(x => x.Number).Distinct().Count() != trip.Itinerary.Count ||
            trip.Itinerary.Any(x => x.Number < 1 || x.Number > trip.Days || x.Stops.Count > 20))
            throw new DomainException("Nieprawidłowe dni planu.");
        var stops = trip.Itinerary.SelectMany(x => x.Stops).ToList();
        if (stops.Select(x => x.Id).Distinct().Count() != stops.Count || stops.Any(x =>
                x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 200 ||
                x.DurationMinutes is < 1 or > 720 || x.TravelMinutes is < 0 or > 720 ||
                x.EstimatedCostPln is < 0 or > 1_000_000 || !double.IsFinite(x.Latitude) ||
                !double.IsFinite(x.Longitude) || x.Latitude is < -90 or > 90 || x.Longitude is < -180 or > 180))
            throw new DomainException("Nieprawidłowy punkt planu.");
        if (trip.Expenses.Select(x => x.Id).Distinct().Count() != trip.Expenses.Count ||
            trip.Expenses.Any(x => x.Id == Guid.Empty || x.Amount is <= 0 or > 10_000_000 ||
                x.RateToPln is <= 0 or > 100000 || x.RateSource?.Length > 200 || string.IsNullOrWhiteSpace(x.Description) ||
                x.Description.Length > 200 || string.IsNullOrWhiteSpace(x.PaidBy) || x.PaidBy.Length > 100 || !Enum.IsDefined(x.Category) ||
                x.Currency.Length != 3 || x.Currency.Any(c => c is < 'A' or > 'Z')))
            throw new DomainException("Sprawdź kwotę, kurs, walutę i opis wydatku.");
        if (trip.PackingList.Select(x => x.Id).Distinct().Count() != trip.PackingList.Count ||
            trip.PackingList.Any(x => x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Title) ||
                x.Title.Length > 200 || x.Category.Length > 60))
            throw new DomainException("Nieprawidłowa lista przygotowań.");
    }

    public static void ValidateDestination(Destination destination)
    {
        if (string.IsNullOrWhiteSpace(destination.Id) || destination.Id.Length > 80 ||
            string.IsNullOrWhiteSpace(destination.Name) || destination.Name.Length > 200 ||
            destination.CountryCode is not { Length: 2 } || destination.CountryCode.Any(c => c is < 'A' or > 'Z') ||
            destination.Location is null || !ValidCoordinate(destination.Location) ||
            destination.Attractions is null || destination.Attractions.Length > 200 ||
            destination.Attractions.Any(a => a is null || a.Location is null || !ValidCoordinate(a.Location) ||
                string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 200 || a.DurationMinutes is < 1 or > 720))
            throw new DomainException("Nieprawidłowe dane kierunku.");
        try { TimeZoneInfo.FindSystemTimeZoneById(destination.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new DomainException("Nieprawidłowa strefa czasowa kierunku."); }
    }
    public static bool ValidCoordinate(Coordinate c) => double.IsFinite(c.Latitude) && double.IsFinite(c.Longitude)
        && c.Latitude is >= -90 and <= 90 && c.Longitude is >= -180 and <= 180;
}

