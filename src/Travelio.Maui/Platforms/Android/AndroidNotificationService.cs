using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Travelio.Application;
using Travelio.Domain;
using Travelio.Application.Localization;
namespace Travelio.Maui.Platforms.Android;

public sealed class AndroidNotificationService : INotificationService
{
    internal const string ChannelId = "travelio-itinerary";
    private readonly Context _context = global::Android.App.Application.Context;
    public async Task<bool> RequestPermissionAsync()
    {
        CreateChannel();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return await Permissions.RequestAsync<NotificationPermission>() == PermissionStatus.Granted;
        return NotificationManagerCompat.From(_context).AreNotificationsEnabled();
    }
    public Task ScheduleAsync(IReadOnlyList<Reminder> reminders)
    {
        CreateChannel();
        var manager = (AlarmManager)_context.GetSystemService(Context.AlarmService)!;
        var saved = new List<string>();
        foreach (var reminder in reminders)
        {
            var intent = IntentFor(reminder.StopId.ToString());
            intent.PutExtra("title", reminder.Title);
            intent.PutExtra("caption", L.T("Twój plan Travelio"));
            var pending = PendingIntent.GetBroadcast(_context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
            // Inexact alarms respect platform battery policies without special exact-alarm access.
            manager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, reminder.DueAt.ToUnixTimeMilliseconds(), pending);
            saved.Add(reminder.StopId.ToString());
        }
        Preferences.Default.Set("travelio.notification.ids", string.Join(",", saved));
        return Task.CompletedTask;
    }
    public Task CancelAsync()
    {
        var manager = (AlarmManager)_context.GetSystemService(Context.AlarmService)!;
        foreach (var id in Preferences.Default.Get("travelio.notification.ids", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            using var pending = PendingIntent.GetBroadcast(_context, 0, IntentFor(id), PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
            if (pending is not null) { manager.Cancel(pending); pending.Cancel(); }
        }
        Preferences.Default.Remove("travelio.notification.ids");
        return Task.CompletedTask;
    }
    private Intent IntentFor(string id)
    {
        var intent = new Intent(_context, typeof(TripReminderReceiver));
        intent.SetData(global::Android.Net.Uri.Parse("travelio://reminder/" + id));
        return intent;
    }
    private void CreateChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var manager = (NotificationManager)_context.GetSystemService(Context.NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(ChannelId, L.T("Plan podróży"), NotificationImportance.Default)
        { Description = "Przypomnienia o nieodhaczonych punktach Twojego planu." });
    }
}
public sealed class NotificationPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions => [("android.permission.POST_NOTIFICATIONS", true)];
}
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class TripReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null || !NotificationManagerCompat.From(context).AreNotificationsEnabled()) return;
        var openApp = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        var builder = new NotificationCompat.Builder(context, AndroidNotificationService.ChannelId)
            .SetSmallIcon(Resource.Drawable.notification_icon).SetContentTitle(intent.GetStringExtra("caption") ?? "Travelio")
            .SetContentText(intent.GetStringExtra("title") ?? "Sprawdź swój plan podróży").SetAutoCancel(true);
        if (openApp is not null) builder.SetContentIntent(PendingIntent.GetActivity(context, 0, openApp, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
        NotificationManagerCompat.From(context).Notify(intent.DataString, 1, builder.Build());
    }
}
