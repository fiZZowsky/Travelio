namespace Travelio.Domain;

public abstract class Entity
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public enum TravelPace { Relaxed, Balanced, Intensive }
public enum ExpenseCategory { Transport, Accommodation, Food, Attractions, Shopping, Other }
public enum TravelStyle { Culture, Nature, Beach, Food, Adventure }
public enum ParticipantRole { Viewer, Editor }

public sealed class Trip : Entity
{
    public string Name { get; set; } = "";
    public string DestinationId { get; set; } = "";
    public Destination? DestinationSnapshot { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddDays(14);
    public int Days { get; set; } = 5;
    public int Travelers { get; set; } = 1;
    public decimal Budget { get; set; } = 5000;
    public TravelPace Pace { get; set; } = TravelPace.Balanced;
    public string Notes { get; set; } = "";
    public List<PlanDay> Itinerary { get; set; } = [];
    public List<Expense> Expenses { get; set; } = [];
    public List<PackingItem> PackingList { get; set; } = [];
    public bool IsDeleted { get; set; }
    public DateOnly EndDate => StartDate.AddDays(Days - 1);
    public decimal Spent => Expenses.Sum(x => x.AmountPln);

    public void AddExpense(Expense expense)
    {
        if (expense.Amount <= 0 || expense.RateToPln <= 0)
            throw new DomainException("Kwota i kurs muszą być większe od zera.");
        if (string.IsNullOrWhiteSpace(expense.Description))
            throw new DomainException("Podaj opis wydatku.");
        if (Expenses.Any(x => x.Id == expense.Id)) return;
        Expenses.Add(expense);
    }
}

public sealed class PlanDay
{
    public int Number { get; set; }
    public List<PlanStop> Stops { get; set; } = [];
}

public sealed class PlanStop : Entity
{
    public string AttractionId { get; set; } = "";
    public string Title { get; set; } = "";
    public TimeOnly StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public int TravelMinutes { get; set; }
    public decimal? EstimatedCostPln { get; set; }
    public string? SourceUrl { get; set; }
    public string? OpeningHours { get; set; }
    public Dictionary<string, string>? LocalizedNames { get; set; }
    public string? OriginalTitle { get; set; }
    public VisitDuration? VisitDuration { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool Completed { get; set; }
}

public sealed class Expense : Entity
{
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    // The recorded exchange rate never changes historical analytics.
    public decimal RateToPln { get; set; } = 1;
    public DateOnly? RateDate { get; set; }
    public string? RateSource { get; set; }
    public ExpenseCategory Category { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string PaidBy { get; set; } = "Ja";
    public decimal AmountPln => decimal.Round(Amount * RateToPln, 2, MidpointRounding.AwayFromZero);
}

public sealed class PackingItem : Entity
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Packed { get; set; }
}

public sealed record Coordinate(double Latitude, double Longitude);
public enum VisitDurationBasis { CategoryEstimate, SourceRecommendation, UserEstimate }
public sealed record VisitDuration(int Minutes, VisitDurationBasis Basis, string? SourceUrl = null, DateTimeOffset? CheckedAt = null);
public sealed record Attraction(string Id, string Name, string Description, TravelStyle Style,
    Coordinate Location, int DurationMinutes, decimal? CostPln, TimeOnly OpensAt, TimeOnly ClosesAt,
    string? SourceUrl = null, string? OpeningHours = null,
    Dictionary<string, string>? LocalizedNames = null, string? OriginalName = null, string? WikidataId = null, VisitDuration? VisitDuration = null);
public sealed record Destination(string Id, string Name, string Country, string CountryCode,
    string Region, string Description, string TimeZoneId, decimal DailyBudgetPln,
    Coordinate Location, string Image, TravelStyle[] Styles, int[] BestMonths, Attraction[] Attractions,
    string? SourceUrl = null, DateTimeOffset? RetrievedAt = null);
public sealed record TravelerPreferences(decimal DailyBudgetPln, int Days, TravelPace Pace,
    TravelStyle Style, int Month, string Region = "");
public sealed record DestinationRecommendation(Destination Destination, int Match, string[] Reasons);
public sealed record Review(string Author, decimal Rating, string Text);
public sealed record TravelOffer(string Id, string Name, string Kind, string Description, decimal PricePln,
    decimal Rating, int ReviewCount, Review[] Reviews, string? BookingUrl = null, bool IsDemo = true);
public sealed record RegionalAlert(string Title, string Description, string Severity,
    string SourceUrl, DateTimeOffset? CheckedAt, bool IsVerified);
public sealed record PreparationAdvice(string Title, string Description, string? SourceUrl = null);
public sealed record Reminder(Guid StopId, string Title, DateTimeOffset DueAt);
public sealed record BudgetSummary(decimal Budget, decimal Spent, decimal Remaining,
    decimal PerDay, decimal PerPerson, IReadOnlyDictionary<ExpenseCategory, decimal> Categories);
public sealed class DomainException(string message) : Exception(message);

