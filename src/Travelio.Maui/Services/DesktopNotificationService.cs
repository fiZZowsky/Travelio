using Travelio.Application;
using Travelio.Domain;
namespace Travelio.Maui.Services;

/// <summary>Foreground reminders on desktop; mobile hosts use native scheduled notifications.</summary>
public sealed class DesktopNotificationService : INotificationService, IDisposable
{
    private Timer? _timer;
    private IReadOnlyList<Reminder> _reminders = [];
    private readonly HashSet<Guid> _notified = [];
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
    public Task ScheduleAsync(IReadOnlyList<Reminder> reminders)
    {
        _reminders = reminders;
        _timer?.Dispose();
        _timer = new Timer(_ =>
        {
            foreach (var reminder in _reminders.Where(x => x.DueAt <= DateTimeOffset.UtcNow && x.DueAt > DateTimeOffset.UtcNow.AddMinutes(-30)))
            {
                lock (_notified) if (!_notified.Add(reminder.StopId)) continue;
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (Microsoft.Maui.Controls.Application.Current?.MainPage is { } page)
                        await page.DisplayAlert("Twój plan Travelio", reminder.Title, "Dobrze");
                });
            }
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        return Task.CompletedTask;
    }
    public Task CancelAsync() { _timer?.Dispose(); _timer = null; _reminders = []; return Task.CompletedTask; }
    public void Dispose() => _timer?.Dispose();
}
