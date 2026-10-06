using Travelio.Application.Localization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Travelio.Application;
using Travelio.Domain;

namespace Travelio.UI.Services;

public sealed class ApiClient(HttpClient http)
{
    private string? _csrf;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    public async Task<UserInfo?> GetUserAsync()
    {
        using var response = await SendGetAsync("api/auth/me");
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        await CheckAsync(response);
        return await response.Content.ReadFromJsonAsync<UserInfo>();
    }
    public async Task LoginAsync(string email, string password)
    {
        await SendAsync(HttpMethod.Post, "api/auth/login", new LoginRequest(email, password));
        _csrf = null;
    }
    public Task RegisterAsync(string email, string password) =>
        SendAsync(HttpMethod.Post, "api/auth/register", new RegisterRequest(email, password));
    public async Task LogoutAsync()
    {
        await SendAsync(HttpMethod.Post, "api/auth/logout", new { });
        _csrf = null;
    }
    public async Task<List<TripEnvelope>> GetTripsAsync() =>
        await GetAsync<List<TripEnvelope>>("api/trips/") ?? [];
    public async Task<TripEnvelope> SaveTripAsync(Trip trip, long version)
    {
        using var response = await SendCoreAsync(HttpMethod.Put, $"api/trips/{trip.Id}", new SaveTripRequest(trip, version));
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new TripConflictException(await response.Content.ReadFromJsonAsync<TripEnvelope>());
        await CheckAsync(response);
        return (await response.Content.ReadFromJsonAsync<TripEnvelope>())!;
    }
    public Task<List<ParticipantInfo>?> GetMembersAsync(Guid tripId) => GetAsync<List<ParticipantInfo>>($"api/trips/{tripId}/members");
    public Task ShareAsync(Guid tripId, string email, ParticipantRole role) =>
        SendAsync(HttpMethod.Post, $"api/trips/{tripId}/members", new ShareTripRequest(email, role));
    public Task RemoveMemberAsync(Guid tripId, string memberId) =>
        SendAsync(HttpMethod.Delete, $"api/trips/{tripId}/members/{Uri.EscapeDataString(memberId)}", new { });
    public async Task<PassportEnvelope> GetPassportAsync() =>
        (await GetAsync<PassportEnvelope>("api/passport/"))!;
    public async Task<PassportEnvelope> SavePassportAsync(List<string> countries, long version)
    {
        using var response = await SendCoreAsync(HttpMethod.Put, "api/passport/", new SavePassportRequest(countries, version));
        await CheckAsync(response);
        return (await response.Content.ReadFromJsonAsync<PassportEnvelope>())!;
    }
    private async Task<T?> GetAsync<T>(string path)
    {
        using var response = await SendGetAsync(path);
        await CheckAsync(response);
        return await response.Content.ReadFromJsonAsync<T>();
    }
    private async Task SendAsync<T>(HttpMethod method, string path, T body)
    {
        using var response = await SendCoreAsync(method, path, body);
        await CheckAsync(response);
    }
    private async Task<HttpResponseMessage> SendCoreAsync<T>(HttpMethod method, string path, T body)
    {
        await _writeGate.WaitAsync();
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                if (_csrf is null)
                {
                    var tokens = await GetAsync<JsonElement>("api/auth/antiforgery");
                    _csrf = tokens.GetProperty("token").GetString();
                }
                using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
                request.Headers.Add("X-Travelio-CSRF", _csrf);
                var response = await http.SendAsync(request);
                // Only retry a request rejected before it reached an endpoint. Never replay uncertain writes.
                if (attempt == 0 && response.StatusCode == HttpStatusCode.BadRequest &&
                    response.Headers.Contains("X-Travelio-Csrf-Expired"))
                { response.Dispose(); _csrf = null; continue; }
                return response;
            }
        }
        finally { _writeGate.Release(); }
    }
    private async Task<HttpResponseMessage> SendGetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await http.SendAsync(request);
    }
    private static async Task CheckAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string? message = null;
        try { message = (await response.Content.ReadFromJsonAsync<ApiError>())?.Message; }
        catch (JsonException) { }
        throw new ApiException(response.StatusCode, message ?? response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => L.T("Zaloguj się ponownie, aby synchronizować dane."),
            HttpStatusCode.Forbidden => L.T("Nie masz uprawnień do tej operacji."),
            HttpStatusCode.TooManyRequests => L.T("Zbyt wiele prób. Spróbuj ponownie za minutę."),
            _ => L.T("Nie udało się zapisać danych na serwerze. Zmiany lokalne są zachowane.")
        });
    }
}
public sealed class ApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
public sealed class TripConflictException(TripEnvelope? remote) : Exception(L.T("Ta podróż zmieniła się na innym urządzeniu."))
{
    public TripEnvelope? Remote { get; } = remote;
}

