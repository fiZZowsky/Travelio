using Microsoft.JSInterop;
using Travelio.Application;
using Travelio.Domain;
using System.Text.Json;

namespace Travelio.UI.Services;

public sealed class BrowserLocalStore(IJSRuntime js) : ILocalStore
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var value = await js.InvokeAsync<JsonElement>("travelio.storage.get", cancellationToken, key);
        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? default : value.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) =>
        await js.InvokeVoidAsync("travelio.storage.set", cancellationToken, key, value);
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        await js.InvokeVoidAsync("travelio.storage.remove", cancellationToken, key);
}
public sealed class BrowserNotificationService(IJSRuntime js) : INotificationService
{
    public async Task<bool> RequestPermissionAsync() => await js.InvokeAsync<bool>("travelio.notifications.request");
    public async Task ScheduleAsync(IReadOnlyList<Reminder> reminders) =>
        await js.InvokeVoidAsync("travelio.notifications.schedule", reminders);
    public async Task CancelAsync() => await js.InvokeVoidAsync("travelio.notifications.cancel");
}

