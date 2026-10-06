using Travelio.Application.Localization;
using System.Net.Http.Json;
using Travelio.Application;
using Travelio.Domain;

namespace Travelio.UI.Services;

/// <summary>Online provider with an explicitly stale offline snapshot. Never labels a failed fetch as safe.</summary>
public sealed class CachedAlertProvider(HttpClient http, ILocalStore store) : IRegionalAlertProvider
{
    public async Task<IReadOnlyList<RegionalAlert>> GetAlertsAsync(Destination destination, CancellationToken cancellationToken = default)
    {
        var key = "travelio.alerts." + destination.Id;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            var alerts = await http.GetFromJsonAsync<List<RegionalAlert>>($"api/destinations/{destination.Id}/alerts", timeout.Token) ?? [];
            if (alerts.Any(x => x.IsVerified))
            {
                await store.SetAsync(key, alerts, cancellationToken);
                return alerts;
            }
            return alerts;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            var cached = await store.GetAsync<List<RegionalAlert>>(key, cancellationToken);
            if (cached is { Count: > 0 }) return cached.Select(x => x with
            {
                Title = "Kopia offline · " + x.Title,
                Description = L.T("Dane mogą być nieaktualne. ") + x.Description,
                IsVerified = false
            }).ToArray();
            return await new UnconfiguredRegionalAlertProvider().GetAlertsAsync(destination, cancellationToken);
        }
    }
}

