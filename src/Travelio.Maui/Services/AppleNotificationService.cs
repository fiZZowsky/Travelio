#if IOS || MACCATALYST
using Foundation;
using UserNotifications;
using Travelio.Application;
using Travelio.Domain;
using Travelio.Application.Localization;
namespace Travelio.Maui.Services;

public sealed class AppleNotificationService : INotificationService
{
    private static readonly TravelNotificationDelegate Delegate = new();
    public async Task<bool> RequestPermissionAsync()
    {
        UNUserNotificationCenter.Current.Delegate = Delegate;
        var result = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound);
        return result.Item1;
    }
    public async Task ScheduleAsync(IReadOnlyList<Reminder> reminders)
    {
        UNUserNotificationCenter.Current.Delegate = Delegate;
        foreach (var reminder in reminders.Take(60))
        {
            var date = reminder.DueAt.UtcDateTime;
            var content = new UNMutableNotificationContent
            {
                Title = L.T("Twój plan Travelio"), Body = reminder.Title + L.T(". Oznacz punkt jako odwiedzony, gdy będzie już za Tobą."),
                Sound = UNNotificationSound.Default
            };
            var components = new NSDateComponents
            {
                Year = date.Year, Month = date.Month, Day = date.Day, Hour = date.Hour,
                Minute = date.Minute, Second = date.Second, TimeZone = NSTimeZone.FromName("UTC")
            };
            await UNUserNotificationCenter.Current.AddNotificationRequestAsync(UNNotificationRequest.FromIdentifier(
                "travelio-" + reminder.StopId, content, UNCalendarNotificationTrigger.CreateTrigger(components, false)));
        }
    }
    public Task CancelAsync() { UNUserNotificationCenter.Current.RemoveAllPendingNotificationRequests(); return Task.CompletedTask; }
    private sealed class TravelNotificationDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler) =>
            completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound);
    }
}
#endif
