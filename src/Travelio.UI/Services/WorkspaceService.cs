using Travelio.Application.Localization;
using System.Net;
using System.Text.Json;
using Travelio.Application;
using Travelio.Application.Services;
using Travelio.Domain;

namespace Travelio.UI.Services;

public sealed class LocalTrip
{
    public Trip Trip { get; set; } = new();
    public long Version { get; set; }
    public bool CanEdit { get; set; } = true;
    public bool IsOwner { get; set; } = true;
    public bool Pending { get; set; }
    public TripEnvelope? Conflict { get; set; }
}
public sealed class Workspace
{
    public List<LocalTrip> Trips { get; set; } = [];
    public List<string> Countries { get; set; } = [];
    public Dictionary<string, bool> CountryChanges { get; set; } = [];
    public long PassportVersion { get; set; }
    public Dictionary<Guid, Guid> ImportedTripIds { get; set; } = [];
}

/// <summary>Offline-first application facade. Serializes persistence and synchronization; never discards conflicts.</summary>
public sealed class WorkspaceService(ILocalStore store, ApiClient api, IDestinationCatalog catalog,
    IItineraryPlanner planner, IPreparationService preparation)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private Workspace _data = new();
    private bool _initialized;
    public event Action? Changed;
    public UserInfo? User { get; private set; }
    public bool SessionValid { get; private set; }
    /// <summary>Whether the Travelio API responded. This does not measure internet connectivity.</summary>
    public bool IsOnline { get; private set; }
    public bool DeviceOnline { get; private set; } = true;
    public bool ConnectionChecked { get; private set; }
    public string ConnectionLabel => !DeviceOnline ? L.T("Offline · zapis lokalny") : !ConnectionChecked ? L.T("Sprawdzam połączenie…") :
        !IsOnline ? L.T("Serwer niedostępny") : User is null ? L.T("Gość · zapis lokalny") : !SessionValid ? L.T("Zaloguj się ponownie") :
        IsSyncing ? L.T("Synchronizacja…") : PendingCount > 0 ? L.T("Zmiany zapisane lokalnie") : L.T("Połączono z kontem");
    private string UnavailableStatus => DeviceOnline
        ? L.T("Serwer Travelio jest niedostępny. Możesz nadal planować i zapisywać zmiany na urządzeniu.")
        : L.T("Urządzenie zgłasza brak sieci. Zapisane plany, wydatki i kraje są dostępne offline.");
    public bool IsSyncing { get; private set; }
    public string Status { get; private set; } = L.T("Gość · zapis lokalny · zaloguj się, aby synchronizować");
    public IReadOnlyList<LocalTrip> Trips => _data.Trips.Where(x => !x.Trip.IsDeleted).ToArray();
    public IReadOnlyList<string> Countries => _data.Countries;
    public int PendingCount => _data.Trips.Count(x => x.Pending) + _data.CountryChanges.Count;
    public bool HasConflicts => _data.Trips.Any(x => x.Conflict is not null);
    public IReadOnlyList<LocalTrip> Conflicts => _data.Trips.Where(x => x.Conflict is not null).ToArray();
    private string Key => $"travelio.v1.{User?.Id ?? "guest"}";

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        User = await store.GetAsync<UserInfo>("travelio.user");
        _data = await store.GetAsync<Workspace>(Key) ?? new Workspace();
        await PersistAsync();
        _initialized = true;
        Changed?.Invoke();
    }

    public async Task ConnectAsync()
    {
        if (!await _connectionGate.WaitAsync(0)) return;
        try
        {
            if (!DeviceOnline) { IsOnline = false; Status = UnavailableStatus; return; }
            var current = await api.GetUserAsync();
            IsOnline = true;
            SessionValid = current is not null;
            if (current is not null && current.Id != User?.Id)
                await SwitchUserAsync(current);
            else if (User is not null && current is null)
                Status = L.T("Zaloguj się ponownie · dane lokalne są dostępne");
            else if (current is null)
                Status = L.T("Serwer dostępny · plany gościa zapisujemy na urządzeniu");
            if (SessionValid) await SyncAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { IsOnline = false; Status = UnavailableStatus; }
        catch (ApiException ex) { IsOnline = (int)ex.StatusCode < 500; Status = ex.Message; }
        finally { ConnectionChecked = true; _connectionGate.Release(); Changed?.Invoke(); }
    }

    public void SetDeviceConnectivity(bool online)
    {
        DeviceOnline = online;
        if (!online) { IsOnline = false; Status = UnavailableStatus; }
        Changed?.Invoke();
    }

    public Trip? Find(Guid id) => _data.Trips.FirstOrDefault(x => x.Trip.Id == id) is { } item ? Copy(item.Trip) : null;
    public LocalTrip? Metadata(Guid id) => _data.Trips.FirstOrDefault(x => x.Trip.Id == id);

    public Trip CreateTrip(string name, string destinationId, DateOnly date, int days, int travelers, decimal budget, TravelPace pace)
    {
        var destination = catalog.Get(destinationId);
        var trip = new Trip
        {
            Name = name.Trim(), DestinationId = destinationId, DestinationSnapshot = destination, StartDate = date,
            Days = days, Travelers = travelers, Budget = budget, Pace = pace,
            PackingList = preparation.CreateChecklist(destination).ToList()
        };
        TripValidator.Validate(trip, catalog);
        trip.Itinerary = planner.Generate(destination, days, pace, travelers).ToList();
        return trip;
    }

    public async Task SaveAsync(Trip trip, long? expectedVersion = null)
    {
        TripValidator.Validate(trip, catalog);
        await _gate.WaitAsync();
        try
        {
            var existing = Metadata(trip.Id);
            if (expectedVersion is not null && existing?.Version != expectedVersion)
                throw new DomainException(L.T("Ta podróż została zaktualizowana w tle. Wczytaj zapisaną wersję, zanim ponownie zapiszesz zmiany."));
            if (existing is not null && !existing.CanEdit) throw new DomainException(L.T("Masz dostęp tylko do odczytu."));
            if (trip.IsDeleted && existing is not null && !existing.IsOwner)
                throw new DomainException(L.T("Tylko właściciel może usunąć podróż."));
            var snapshot = Copy(_data);
            if (existing is null) _data.Trips.Insert(0, new() { Trip = Copy(trip), Pending = true });
            else { existing.Trip = Copy(trip); existing.Pending = true; }
            try { await PersistAsync(); }
            catch { _data = snapshot; throw; }
            Status = User is null ? L.T("Gość · zapisano na urządzeniu") : L.T("Zapisano lokalnie · oczekuje na synchronizację");
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    public async Task ToggleCountryAsync(string code)
    {
        await _gate.WaitAsync();
        try
        {
            var snapshot = Copy(_data);
            var visited = !_data.Countries.Contains(code);
            if (visited) _data.Countries.Add(code); else _data.Countries.Remove(code);
            _data.CountryChanges[code] = visited;
            try { await PersistAsync(); }
            catch { _data = snapshot; throw; }
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    public async Task LoginAsync(string email, string password, bool register)
    {
        await _connectionGate.WaitAsync();
        try
        {
            if (register) await api.RegisterAsync(email.Trim(), password);
            await api.LoginAsync(email.Trim(), password);
            var user = await api.GetUserAsync() ?? throw new DomainException(L.T("Sesja nie została zapisana. Zezwól na pliki cookie dla Travelio i spróbuj ponownie."));
            await SwitchUserAsync(user);
            SessionValid = true;
            IsOnline = true;
            ConnectionChecked = true;
            await SyncAsync();
        }
        finally { _connectionGate.Release(); }
    }

    private async Task SwitchUserAsync(UserInfo user)
    {
        await _gate.WaitAsync();
        try
        {
            var next = await store.GetAsync<Workspace>($"travelio.v1.{user.Id}") ?? new Workspace();
            await store.SetAsync("travelio.user", user);
            User = user;
            _data = next;
            Status = L.T("Konto połączone");
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    public async Task LogoutAsync()
    {
        if (PendingCount > 0) throw new DomainException(L.T("Najpierw zsynchronizuj zmiany lub wyeksportuj dane. Wylogowanie usuwa lokalną kopię konta."));
        await api.LogoutAsync();
        await _gate.WaitAsync();
        try
        {
            await store.RemoveAsync(Key);
            await store.RemoveAsync("travelio.user");
            User = null;
            SessionValid = false;
            _data = await store.GetAsync<Workspace>(Key) ?? new Workspace();
            Status = L.T("Wylogowano · tryb lokalny");
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    public async Task SyncAsync()
    {
        if (User is null) { Status = IsOnline ? L.T("Plany gościa zapisujemy na urządzeniu. Konto umożliwia synchronizację.") : UnavailableStatus; Changed?.Invoke(); return; }
        if (!await _gate.WaitAsync(0)) return;
        IsSyncing = true;
        Changed?.Invoke();
        try
        {
            var current = await api.GetUserAsync();
            if (current?.Id != User.Id) throw new ApiException(HttpStatusCode.Unauthorized, L.T("Zaloguj się ponownie do tego konta, aby synchronizować."));
            SessionValid = true;
            foreach (var local in _data.Trips.Where(x => x.Pending && x.Conflict is null).ToList())
            {
                try
                {
                    var remote = await api.SaveTripAsync(local.Trip, local.Version);
                    local.Version = remote.Version;
                    local.CanEdit = remote.CanEdit;
                    local.IsOwner = remote.IsOwner;
                    local.Pending = false;
                    // Persist each acknowledged write before attempting the next one.
                    await PersistAsync();
                }
                catch (TripConflictException conflict) { local.Conflict = conflict.Remote; await PersistAsync(); }
                catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    // Retain edits after access revocation as a detached, private copy.
                    var copy = Copy(local.Trip);
                    copy = CloneWithNewId(copy);
                    copy.Name = $"Kopia · {copy.Name}"[..Math.Min(100, copy.Name.Length + 8)];
                    local.Trip = copy;
                    local.Version = 0;
                    local.IsOwner = local.CanEdit = true;
                    await PersistAsync();
                }
            }
            var remoteTrips = await api.GetTripsAsync();
            foreach (var remote in remoteTrips)
            {
                var local = Metadata(remote.Trip.Id);
                if (local?.Pending == true) continue;
                if (local is not null) _data.Trips.Remove(local);
                _data.Trips.Add(new() { Trip = remote.Trip, Version = remote.Version, CanEdit = remote.CanEdit, IsOwner = remote.IsOwner });
            }
            var remoteIds = remoteTrips.Select(x => x.Trip.Id).ToHashSet();
            _data.Trips.RemoveAll(x => !x.Pending && !remoteIds.Contains(x.Trip.Id));

            // Rebase country toggles on the newest passport. Independent offline edits merge.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var passport = await api.GetPassportAsync();
                var countries = passport.Countries.ToHashSet();
                foreach (var (code, visited) in _data.CountryChanges)
                    if (visited) countries.Add(code); else countries.Remove(code);
                try
                {
                    if (_data.CountryChanges.Count > 0)
                        passport = await api.SavePassportAsync(countries.Order().ToList(), passport.Version);
                    _data.Countries = passport.Countries;
                    _data.PassportVersion = passport.Version;
                    _data.CountryChanges.Clear();
                    break;
                }
                catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict && attempt < 2) { }
            }
            await PersistAsync();
            IsOnline = true;
            Status = HasConflicts ? L.T("Wybierz wersję podróży · wykryto konflikt") :
                PendingCount > 0 ? L.T("Pozostały zmiany lokalne · synchronizuj ponownie") : L.T("Wszystko zsynchronizowane");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { IsOnline = false; Status = UnavailableStatus; }
        catch (ApiException ex)
        { if (ex.StatusCode == HttpStatusCode.Unauthorized) SessionValid = false; Status = ex.Message; }
        finally { IsSyncing = false; _gate.Release(); Changed?.Invoke(); }
    }

    public async Task ResolveConflictAsync(Guid id, bool keepLocalCopy)
    {
        await _gate.WaitAsync();
        try
        {
            var local = Metadata(id);
            if (local?.Conflict is not { } remote) return;
            if (keepLocalCopy)
            {
                var copy = CloneWithNewId(local.Trip);
                copy.Name = $"Kopia · {copy.Name}"[..Math.Min(100, copy.Name.Length + 8)];
                _data.Trips.Add(new() { Trip = copy, Pending = true });
            }
            local.Trip = remote.Trip;
            local.Version = remote.Version;
            local.CanEdit = remote.CanEdit;
            local.IsOwner = remote.IsOwner;
            local.Pending = false;
            local.Conflict = null;
            await PersistAsync();
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    public string ExportJson() => JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });

    public async Task<int> GuestTripCountAsync() =>
        (await store.GetAsync<Workspace>("travelio.v1.guest"))?.Trips.Count(x => !x.Trip.IsDeleted) ?? 0;

    public async Task<int> ImportGuestAsync()
    {
        if (User is null) throw new DomainException(L.T("Najpierw zaloguj się na konto."));
        return await ImportAsync(await store.GetAsync<Workspace>("travelio.v1.guest") ?? new Workspace());
    }

    public async Task<int> ImportJsonAsync(string json)
    {
        if (json.Length > 5 * 1024 * 1024) throw new DomainException(L.T("Kopia może mieć maksymalnie 5 MB."));
        try
        {
            using var document = JsonDocument.Parse(json);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            if (document.RootElement.TryGetProperty("Trips", out _) || document.RootElement.TryGetProperty("trips", out _))
                return await ImportAsync(JsonSerializer.Deserialize<Workspace>(json, options) ?? throw new JsonException());
            var trip = JsonSerializer.Deserialize<Trip>(json, options) ?? throw new JsonException();
            return await ImportAsync(new Workspace { Trips = [new LocalTrip { Trip = trip }] });
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        { throw new DomainException(L.T("Ten plik nie jest poprawną kopią danych Travelio.")); }
    }

    private async Task<int> ImportAsync(Workspace source)
    {
        if (source.Trips is null || source.Countries is null || source.Trips.Count > 200 || source.Countries.Count > 250 ||
            source.Trips.Any(x => x?.Trip is null) || source.Countries.Any(c => c is not { Length: 2 } || c.Any(ch => ch is < 'A' or > 'Z')))
            throw new DomainException(L.T("Kopia ma niepoprawną strukturę lub przekracza limit 200 podróży."));
        // Validate the entire backup before changing any current data.
        foreach (var item in source.Trips) TripValidator.Validate(item.Trip, catalog);
        await _gate.WaitAsync();
        var before = Copy(_data);
        var imported = 0;
        try
        {
            foreach (var item in source.Trips.Where(x => !x.Trip.IsDeleted))
            {
                if (_data.ImportedTripIds.ContainsKey(item.Trip.Id) || Metadata(item.Trip.Id) is not null) continue;
                var copy = CloneWithNewId(item.Trip);
                _data.Trips.Add(new LocalTrip { Trip = copy, Pending = true });
                _data.ImportedTripIds[item.Trip.Id] = copy.Id;
                imported++;
            }
            foreach (var code in source.Countries.Except(_data.Countries).ToArray())
            { _data.Countries.Add(code); _data.CountryChanges[code] = true; }
            await PersistAsync();
            Status = L.T("Przywrócono kopię na urządzeniu. Istniejące plany pozostały bez zmian.");
        }
        catch { _data = before; throw; }
        finally { _gate.Release(); }
        Changed?.Invoke();
        return imported;
    }
    private Task PersistAsync() => store.SetAsync(Key, _data);
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static Trip CloneWithNewId(Trip trip) => new()
    {
        Name = trip.Name, DestinationId = trip.DestinationId, DestinationSnapshot = trip.DestinationSnapshot, StartDate = trip.StartDate, Days = trip.Days,
        Travelers = trip.Travelers, Budget = trip.Budget, Pace = trip.Pace, Notes = trip.Notes,
        Itinerary = Copy(trip.Itinerary), Expenses = Copy(trip.Expenses), PackingList = Copy(trip.PackingList)
    };
}

