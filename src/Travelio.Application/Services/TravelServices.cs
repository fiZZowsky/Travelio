using Travelio.Domain;

namespace Travelio.Application.Services;

public sealed class BudgetService : IBudgetService
{
    public BudgetSummary Analyze(Trip trip) => new(trip.Budget, trip.Spent, trip.Budget - trip.Spent,
        trip.Spent / Math.Max(1, trip.Days), trip.Spent / Math.Max(1, trip.Travelers),
        trip.Expenses.GroupBy(x => x.Category).ToDictionary(x => x.Key, x => x.Sum(e => e.AmountPln)));
}

public sealed class PreparationService : IPreparationService
{
    public IReadOnlyList<PackingItem> CreateChecklist(Destination destination)
    {
        List<PackingItem> items =
        [
            new() { Title = "Sprawdź ważność dokumentu i zasady wjazdu", Category = "Dokumenty" },
            new() { Title = "Ubezpieczenie podróżne i numery alarmowe", Category = "Dokumenty" },
            new() { Title = "Kopie rezerwacji zapisane offline", Category = "Dokumenty" },
            new() { Title = "Leki przyjmowane na stałe i podstawowa apteczka", Category = "Zdrowie" },
            new() { Title = "Sprawdź zalecenia medycyny podróży", Category = "Zdrowie" },
            new() { Title = "Ładowarka i powerbank", Category = "Bagaż" },
            new() { Title = "Wygodne buty i butelka wielorazowa", Category = "Bagaż" }
        ];
        if (destination.Styles.Contains(TravelStyle.Beach))
            items.Add(new() { Title = "Ochrona przeciwsłoneczna i strój kąpielowy", Category = "Bagaż" });
        if (destination.Region != "Europa")
            items.Add(new() { Title = "Sprawdź typ gniazdka, adapter i roaming", Category = "Bagaż" });
        return items;
    }

    public IReadOnlyList<PreparationAdvice> GetAdvice(Destination destination) =>
    [
        new("Dokumenty i zasady wjazdu", $"Sprawdź aktualne warunki wjazdu do kraju: {destination.Country}. Zależą od obywatelstwa, trasy i długości pobytu.", "https://www.gov.pl/web/dyplomacja/informacje-dla-podrozujacych"),
        new("Zdrowie przed podróżą", "Zalecenia i ewentualne wymogi szczepień zależą od trasy oraz sytuacji zdrowotnej. Skonsultuj plan wyjazdu z poradnią medycyny podróży.", "https://www.who.int/health-topics/travel-and-health"),
        new("Plan na nieprzewidziane sytuacje", "Zapisz kontakt do ubezpieczyciela i najbliższej placówki konsularnej. Udostępnij bliskiej osobie plan wyjazdu.", "https://odyseusz.msz.gov.pl/")
    ];
}

public sealed class ScheduleMonitor : IScheduleMonitor
{
    public IReadOnlyList<Reminder> GetReminders(Trip trip, Destination destination, DateTimeOffset now)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(destination.TimeZoneId);
        return trip.Itinerary.SelectMany(day => day.Stops.Where(stop => !stop.Completed).Select(stop =>
        {
            var local = trip.StartDate.AddDays(day.Number - 1).ToDateTime(stop.StartsAt, DateTimeKind.Unspecified);
            // Skip nonexistent wall times during DST transitions.
            if (timezone.IsInvalidTime(local)) return null;
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, timezone);
            return new Reminder(stop.Id, $"Sprawdź plan: {stop.Title}", new DateTimeOffset(utc).AddMinutes(15));
        })).Where(x => x is not null && x.DueAt > now.AddMinutes(-30) && x.DueAt < now.AddDays(30))
           .Select(x => x!).OrderBy(x => x.DueAt).Take(60).ToArray();
    }
}

