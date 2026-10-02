using System.Text.Json;
using Travelio.Application;
using Travelio.Application.Services;
using Travelio.UI.Services;
using Travelio.Domain;
namespace Travelio.Tests;

public sealed class OfflineTests
{
    [Fact]
    public async Task Locally_saved_edits_survive_a_new_application_instance_without_network()
    {
        var store = new MemoryStore();
        var first = Create(store);
        await first.InitializeAsync();
        var trip = await Seed(first);
        trip.Notes = "Zapamiętaj kawiarnię";
        await first.SaveAsync(trip);
        await first.ToggleCountryAsync("JP");
        var second = Create(store);
        await second.InitializeAsync();
        await second.ConnectAsync();
        Assert.False(second.IsOnline);
        Assert.Equal("Zapamiętaj kawiarnię", second.Find(trip.Id)!.Notes);
        Assert.Contains("JP", second.Countries);
    }
    [Fact]
    public async Task Failed_local_write_rolls_back_and_does_not_report_success()
    {
        var store = new MemoryStore();
        var workspace = Create(store);
        await workspace.InitializeAsync();
        var original = await Seed(workspace);
        var trip = workspace.Find(original.Id)!;
        trip.Name = "Nie zapisano";
        store.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => workspace.SaveAsync(trip));
        Assert.NotEqual("Nie zapisano", workspace.Find(trip.Id)!.Name);
    }
    [Fact]
    public async Task Mutating_an_editor_copy_cannot_change_persisted_state_before_save()
    {
        var workspace = Create(new MemoryStore());
        await workspace.InitializeAsync();
        var original = await Seed(workspace);
        var copy = workspace.Find(original.Id)!;
        copy.Expenses.Clear();
        Assert.NotEmpty(workspace.Find(copy.Id)!.Expenses);
    }
    private static WorkspaceService Create(MemoryStore store) =>
        new(store, new ApiClient(new HttpClient(new OfflineHandler()) { BaseAddress = new Uri("https://localhost/") }),
            new DemoDestinationCatalog(), new ItineraryPlanner(), new PreparationService());
    [Fact]
    public async Task A_new_workspace_has_no_fake_trips_expenses_or_visited_countries()
    {
        var workspace = Create(new MemoryStore()); await workspace.InitializeAsync();
        Assert.Empty(workspace.Trips); Assert.Empty(workspace.Countries);
    }
    [Fact]
    public async Task A_city_outside_the_editorial_catalog_survives_an_offline_restart()
    {
        var store = new MemoryStore(); var workspace = Create(store); await workspace.InitializeAsync();
        var trip = PlannerTests.ValidTrip();
        trip.DestinationId = "geo-3094802";
        trip.DestinationSnapshot = new DemoDestinationCatalog().Get("rome") with { Id = trip.DestinationId, Name = "Kraków", TimeZoneId = "Europe/Warsaw", CountryCode = "PL" };
        await workspace.SaveAsync(trip);
        var restored = Create(store); await restored.InitializeAsync();
        Assert.Equal("Kraków", restored.Find(trip.Id)!.DestinationSnapshot!.Name);
        await restored.SaveAsync(restored.Find(trip.Id)!);
    }
    private static async Task<Trip> Seed(WorkspaceService workspace)
    {
        var trip = workspace.CreateTrip("Test", "lisbon", new(2027, 1, 10), 5, 2, 4000, TravelPace.Balanced);
        trip.AddExpense(new() { Description = "Testowy wydatek", Amount = 10 });
        await workspace.SaveAsync(trip); return trip;
    }
    [Fact]
    public async Task Unavailable_api_is_not_reported_as_no_internet()
    {
        var workspace = Create(new MemoryStore()); await workspace.InitializeAsync();
        workspace.SetDeviceConnectivity(true); await workspace.ConnectAsync();
        Assert.True(workspace.DeviceOnline);
        Assert.False(workspace.IsOnline);
        Assert.Equal("Serwer niedostępny", workspace.ConnectionLabel);
        workspace.SetDeviceConnectivity(false);
        Assert.Contains("Offline", workspace.ConnectionLabel);
        var trip = await Seed(workspace);
        Assert.NotNull(workspace.Find(trip.Id));
    }
    [Fact]
    public async Task Restore_makes_private_copies_once_without_overwriting_existing_edits()
    {
        var original = Create(new MemoryStore()); await original.InitializeAsync();
        var trip = await Seed(original); await original.ToggleCountryAsync("PL");
        var destination = Create(new MemoryStore()); await destination.InitializeAsync();
        Assert.Equal(1, await destination.ImportJsonAsync(original.ExportJson()));
        var restored = Assert.Single(destination.Trips);
        Assert.NotEqual(trip.Id, restored.Trip.Id);
        Assert.True(restored.IsOwner && restored.CanEdit && restored.Pending);
        Assert.Equal(0, restored.Version);
        var edited = destination.Find(restored.Trip.Id)!; edited.Name = "Moja zmiana";
        await destination.SaveAsync(edited);
        Assert.Equal(0, await destination.ImportJsonAsync(original.ExportJson()));
        Assert.Equal("Moja zmiana", Assert.Single(destination.Trips).Trip.Name);
        Assert.Contains("PL", destination.Countries);
    }
    [Fact]
    public async Task Restore_validates_whole_backup_and_rolls_back_a_failed_write()
    {
        var store = new MemoryStore(); var workspace = Create(store); await workspace.InitializeAsync();
        var first = PlannerTests.ValidTrip(); var invalid = PlannerTests.ValidTrip(); invalid.Days = 0;
        var json = JsonSerializer.Serialize(new Workspace { Trips = [new() { Trip = first }, new() { Trip = invalid }] });
        await Assert.ThrowsAsync<DomainException>(() => workspace.ImportJsonAsync(json));
        Assert.Empty(workspace.Trips);
        store.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => workspace.ImportJsonAsync(JsonSerializer.Serialize(first)));
        Assert.Empty(workspace.Trips);
    }
    [Fact]
    public async Task Offline_planning_uses_real_saved_places_without_requesting_server()
    {
        var store = new MemoryStore(); var catalog = new DemoDestinationCatalog();
        var client = new TravelDataClient(new HttpClient(new OfflineHandler()) { BaseAddress = new Uri("https://localhost/") }, store, catalog);
        var (city, notice) = await client.ForPlanningAsync(catalog.Get("rome"), false);
        Assert.NotEmpty(city.Attractions); Assert.NotNull(notice);
        var plan = new ItineraryPlanner().Generate(city, 2, TravelPace.Balanced, 1);
        Assert.True(plan.Sum(x => x.Stops.Count) >= 5);
    }
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Offline");
    }
    private sealed class MemoryStore : ILocalStore
    {
        private readonly Dictionary<string,string> _data = [];
        public bool FailWrites { get; set; }
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_data.TryGetValue(key, out var value) ? JsonSerializer.Deserialize<T>(value) : default);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            if (FailWrites) throw new IOException("No space");
            _data[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) { _data.Remove(key); return Task.CompletedTask; }
    }
}

