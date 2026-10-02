using Travelio.Domain;

namespace Travelio.Application;

public interface IDestinationCatalog
{
    IReadOnlyList<Destination> All { get; }
    Destination Get(string id);
    void Register(Destination destination);
    Destination Resolve(Trip trip) => trip.DestinationSnapshot ?? Get(trip.DestinationId);
}
public interface IRecommendationService
{
    IReadOnlyList<DestinationRecommendation> Recommend(TravelerPreferences preferences);
}
public interface IItineraryPlanner
{
    IReadOnlyList<PlanDay> Generate(Destination destination, int days, TravelPace pace, int travelers);
}
public interface IBudgetService
{
    BudgetSummary Analyze(Trip trip);
}
public interface IPreparationService
{
    IReadOnlyList<PackingItem> CreateChecklist(Destination destination);
    IReadOnlyList<PreparationAdvice> GetAdvice(Destination destination);
}
public interface ITravelOfferProvider
{
    Task<IReadOnlyList<TravelOffer>> SearchAsync(Destination destination, CancellationToken cancellationToken = default);
}
public interface IRegionalAlertProvider
{
    Task<IReadOnlyList<RegionalAlert>> GetAlertsAsync(Destination destination, CancellationToken cancellationToken = default);
}
public interface IScheduleMonitor
{
    IReadOnlyList<Reminder> GetReminders(Trip trip, Destination destination, DateTimeOffset now);
}
public interface ILocalStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
public interface INotificationService
{
    Task<bool> RequestPermissionAsync();
    Task ScheduleAsync(IReadOnlyList<Reminder> reminders);
    Task CancelAsync();
}

public sealed record UserInfo(string Id, string Email);
public sealed record LoginRequest(string Email, string Password);
public sealed record RegisterRequest(string Email, string Password);
public sealed record TripEnvelope(Trip Trip, long Version, bool CanEdit, bool IsOwner);
public sealed record SaveTripRequest(Trip Trip, long ExpectedVersion);
public sealed record ShareTripRequest(string Email, ParticipantRole Role);
public sealed record ParticipantInfo(string UserId, string Email, ParticipantRole Role);
public sealed record PassportEnvelope(List<string> Countries, long Version);
public sealed record SavePassportRequest(List<string> Countries, long ExpectedVersion);
public sealed record ApiError(string Message);

