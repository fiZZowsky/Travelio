using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Travelio.Application;
using Travelio.Domain;
using Travelio.Application.Services;
using Travelio.UI.Services;
namespace Travelio.Tests;

public sealed class TravelioFactory : WebApplicationFactory<Program>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "travelio-test-" + Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Development").UseSetting("Travelio:DataPath", _directory);
    public HttpClient NewClient() => CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
    public static async Task Authenticate(HttpClient client, string email)
    {
        (await Send(client, HttpMethod.Post, "/api/auth/register", new RegisterRequest(email, "TravelioTest2026!"))).EnsureSuccessStatusCode();
        (await Send(client, HttpMethod.Post, "/api/auth/login", new LoginRequest(email, "TravelioTest2026!"))).EnsureSuccessStatusCode();
    }
    public static async Task<HttpResponseMessage> Send<T>(HttpClient client, HttpMethod method, string url, T body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/antiforgery");
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Travelio-CSRF", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
}

public sealed class ApiTests
{
    [Fact]
    public async Task Workspace_registration_import_and_restart_preserve_guest_and_cloud_data()
    {
        await using var app = new TravelioFactory(); using var http = app.NewClient();
        var store = new TestStore();
        WorkspaceService Create() => new(store, new ApiClient(http), new DemoDestinationCatalog(), new ItineraryPlanner(), new PreparationService());
        var workspace = Create(); await workspace.InitializeAsync(); await workspace.ConnectAsync();
        Assert.True(workspace.IsOnline); Assert.Null(workspace.User);
        var trip = workspace.CreateTrip("Moja lokalna podróż", "rome", new(2027, 4, 10), 3, 2, 2500, TravelPace.Balanced);
        await workspace.SaveAsync(trip); await workspace.ToggleCountryAsync("PL");
        await workspace.LoginAsync("workspace@example.test", "TravelioTest2026!", register: true);
        Assert.True(workspace.SessionValid);
        Assert.Empty(workspace.Trips);
        Assert.Equal(1, await workspace.ImportGuestAsync());
        await workspace.SyncAsync();
        Assert.Equal(0, workspace.PendingCount);
        var restored = Create(); await restored.InitializeAsync(); await restored.ConnectAsync();
        Assert.Equal(trip.Name, Assert.Single(restored.Trips).Trip.Name);
        Assert.Contains("PL", restored.Countries);
        await restored.LogoutAsync();
        Assert.Equal(trip.Id, Assert.Single(restored.Trips).Trip.Id);
        Assert.Contains("PL", restored.Countries);
    }

    private sealed class TestStore : ILocalStore
    {
        private readonly Dictionary<string, string> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? JsonSerializer.Deserialize<T>(value) : default);
        public Task SetAsync<T>(string key, T value, CancellationToken ct = default)
        { _values[key] = JsonSerializer.Serialize(value); return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken ct = default)
        { _values.Remove(key); return Task.CompletedTask; }
    }
    [Fact]
    public async Task Anonymous_users_cannot_read_trips_and_writes_require_csrf()
    {
        await using var app = new TravelioFactory();
        using var client = app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/trips/")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("csrf@example.test", "TravelioTest2026!"))).StatusCode);
    }
    [Fact]
    public async Task Trips_are_private_and_stale_writes_return_current_version()
    {
        await using var app = new TravelioFactory();
        using var owner = app.NewClient();
        using var stranger = app.NewClient();
        await TravelioFactory.Authenticate(owner, "owner@example.test");
        await TravelioFactory.Authenticate(stranger, "stranger@example.test");
        var trip = PlannerTests.ValidTrip();
        var created = await TravelioFactory.Send(owner, HttpMethod.Put, $"/api/trips/{trip.Id}", new SaveTripRequest(trip, 0));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(1, (await created.Content.ReadFromJsonAsync<TripEnvelope>())!.Version);
        Assert.Empty((await stranger.GetFromJsonAsync<List<TripEnvelope>>("/api/trips/"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await TravelioFactory.Send(stranger, HttpMethod.Put, $"/api/trips/{trip.Id}", new SaveTripRequest(trip, 1))).StatusCode);
        trip.Name = "Wersja druga";
        (await TravelioFactory.Send(owner, HttpMethod.Put, $"/api/trips/{trip.Id}", new SaveTripRequest(trip, 1))).EnsureSuccessStatusCode();
        trip.Name = "Stara wersja offline";
        var stale = await TravelioFactory.Send(owner, HttpMethod.Put, $"/api/trips/{trip.Id}", new SaveTripRequest(trip, 1));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var conflict = await stale.Content.ReadFromJsonAsync<TripEnvelope>();
        Assert.Equal("Wersja druga", conflict!.Trip.Name);
        Assert.Equal(2, conflict.Version);
    }
    [Fact]
    public async Task Viewer_cannot_edit_editor_cannot_delete_or_manage_members()
    {
        await using var app = new TravelioFactory();
        using var owner = app.NewClient();
        using var friend = app.NewClient();
        await TravelioFactory.Authenticate(owner, "owner@example.test");
        await TravelioFactory.Authenticate(friend, "friend@example.test");
        var trip = PlannerTests.ValidTrip();
        (await TravelioFactory.Send(owner, HttpMethod.Put, $"/api/trips/{trip.Id}", new SaveTripRequest(trip, 0))).EnsureSuccessStatusCode();
        (await TravelioFactory.Send(owner, HttpMethod.Post, $"/api/trips/{trip.Id}/members", new ShareTripRequest("friend@example.test", ParticipantRole.Viewer))).EnsureSuccessStatusCode();
        var shared = Assert.Single((await friend.GetFromJsonAsync<List<TripEnvelope>>("/api/trips/"))!);
        Assert.False(shared.CanEdit);
        Assert.Equal(HttpStatusCode.Forbidden, (await TravelioFactory.Send(friend, HttpMethod.Put, $"/api/trips/{trip.Id}", new SaveTripRequest(trip, 1))).StatusCode);
        (await TravelioFactory.Send(owner, HttpMethod.Post, $"/api/trips/{trip.Id}/members", new ShareTripRequest("friend@example.test", ParticipantRole.Editor))).EnsureSuccessStatusCode();
        trip.Name = "Wspólna edycja";
        (await TravelioFactory.Send(friend, HttpMethod.Put, $"/api/trips/{trip.Id}", new SaveTripRequest(trip, 1))).EnsureSuccessStatusCode();
        trip.IsDeleted = true;
        Assert.Equal(HttpStatusCode.Forbidden, (await TravelioFactory.Send(friend, HttpMethod.Put, $"/api/trips/{trip.Id}", new SaveTripRequest(trip, 2))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await TravelioFactory.Send(friend, HttpMethod.Post, $"/api/trips/{trip.Id}/members", new ShareTripRequest("owner@example.test", ParticipantRole.Viewer))).StatusCode);
    }
    [Fact]
    public async Task Revocation_removes_server_access()
    {
        await using var app = new TravelioFactory();
        using var owner = app.NewClient(); using var friend = app.NewClient();
        await TravelioFactory.Authenticate(owner, "owner@example.test"); await TravelioFactory.Authenticate(friend, "friend@example.test");
        var trip = PlannerTests.ValidTrip();
        (await TravelioFactory.Send(owner, HttpMethod.Put, $"/api/trips/{trip.Id}", new SaveTripRequest(trip, 0))).EnsureSuccessStatusCode();
        (await TravelioFactory.Send(owner, HttpMethod.Post, $"/api/trips/{trip.Id}/members", new ShareTripRequest("friend@example.test", ParticipantRole.Editor))).EnsureSuccessStatusCode();
        var member = Assert.Single((await owner.GetFromJsonAsync<List<ParticipantInfo>>($"/api/trips/{trip.Id}/members"))!);
        (await TravelioFactory.Send(owner, HttpMethod.Delete, $"/api/trips/{trip.Id}/members/{member.UserId}", new { })).EnsureSuccessStatusCode();
        Assert.Empty((await friend.GetFromJsonAsync<List<TripEnvelope>>("/api/trips/"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await TravelioFactory.Send(friend, HttpMethod.Put, $"/api/trips/{trip.Id}", new SaveTripRequest(trip, 1))).StatusCode);
    }
    [Fact]
    public async Task Passport_conflict_prevents_overwriting_another_device()
    {
        await using var app = new TravelioFactory(); using var client = app.NewClient();
        await TravelioFactory.Authenticate(client, "passport@example.test");
        (await TravelioFactory.Send(client, HttpMethod.Put, "/api/passport/", new SavePassportRequest(["PL","IT"], 0))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await TravelioFactory.Send(client, HttpMethod.Put, "/api/passport/", new SavePassportRequest(["ES"], 0))).StatusCode);
        var current = await client.GetFromJsonAsync<PassportEnvelope>("/api/passport/");
        Assert.Equal(new[] { "IT", "PL" }, current!.Countries.Order());
    }
    [Fact]
    public async Task Invalid_trip_never_enters_database()
    {
        await using var app = new TravelioFactory(); using var client = app.NewClient();
        await TravelioFactory.Authenticate(client, "invalid@example.test");
        var trip = PlannerTests.ValidTrip(); trip.Days = -1;
        Assert.Equal(HttpStatusCode.BadRequest, (await TravelioFactory.Send(client, HttpMethod.Put, $"/api/trips/{trip.Id}", new SaveTripRequest(trip, 0))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<TripEnvelope>>("/api/trips/"))!);
    }
}

