using Travelio.Application;

namespace Travelio.UI.Services;

public sealed class ReminderCoordinator(WorkspaceService workspace, IDestinationCatalog catalog,
    IScheduleMonitor monitor, INotificationService notifications, ILocalStore store)
{
    private bool _enabled;
    private bool _initialized;
    private string? _fingerprint;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool Enabled => _enabled;
    public async Task<bool> EnableAsync()
    {
        if (!await notifications.RequestPermissionAsync()) return false;
        _enabled = true;
        _initialized = true;
        await store.SetAsync("travelio.reminders.enabled", true);
        await RefreshAsync();
        return true;
    }
    public async Task DisableAsync()
    {
        _enabled = false;
        _fingerprint = null;
        await store.SetAsync("travelio.reminders.enabled", false);
        await notifications.CancelAsync();
    }
    public async Task RefreshAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_initialized) { _enabled = await store.GetAsync<bool>("travelio.reminders.enabled"); _initialized = true; }
            if (!_enabled) return;
            var reminders = workspace.Trips.SelectMany(x => monitor.GetReminders(x.Trip,
                catalog.Resolve(x.Trip), DateTimeOffset.UtcNow)).OrderBy(x => x.DueAt).Take(60).ToArray();
            var fingerprint = string.Join("|", reminders.Select(x => $"{x.StopId}:{x.DueAt:O}:{x.Title}"));
            if (_fingerprint == fingerprint) return;
            await notifications.CancelAsync();
            await notifications.ScheduleAsync(reminders);
            _fingerprint = fingerprint;
        }
        finally { _gate.Release(); }
    }
}

