using Travelio.Application.Localization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Travelio.Application;
using Travelio.Application.Services;
using Travelio.Domain;
using Travelio.Infrastructure;

namespace Travelio.Api;

public static class ApiEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapTravelioEndpoints(this WebApplication app)
    {
        app.MapGet("/api/status", () => Results.Ok(new { application = "Travelio" }));
        app.MapGet("/api/destinations/{id}/alerts", async (string id, ITravelDataProvider data, IRegionalAlertProvider provider, CancellationToken ct) =>
            Results.Ok(await provider.GetAlertsAsync(await data.GetCityAsync(id, ct), ct))).RequireRateLimiting("travel-data");
        var data = app.MapGroup("/api/data").RequireRateLimiting("travel-data");
        data.MapGet("/cities", (string q, ITravelDataProvider p, CancellationToken ct) => p.SearchCitiesAsync(q, ct));
        data.MapGet("/cities/{id}", (string id, ITravelDataProvider p, CancellationToken ct) => p.GetCityAsync(id, ct));
        data.MapGet("/places", (double lat, double lon, ITravelDataProvider p, CancellationToken ct) => p.GetPlacesAsync(new(lat, lon), ct));
        data.MapGet("/search", (string q, double lat, double lon, ITravelDataProvider p, CancellationToken ct) => p.SearchPlacesAsync(q, new(lat, lon), ct));
        data.MapGet("/route", (string points, ITravelDataProvider p, CancellationToken ct) =>
        {
            if (points.Length > 2000) throw new DomainException(L.T("Za długa trasa."));
            var coordinates = points.Split(';').Select(pair =>
            {
                var values = pair.Split(',');
                if (values.Length != 2 || !double.TryParse(values[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)
                    || !double.TryParse(values[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
                    throw new DomainException(L.T("Nieprawidłowa trasa."));
                return new Coordinate(lat, lon);
            }).ToArray();
            return p.GetRouteAsync(coordinates, ct);
        });
        data.MapGet("/rates/{currency}", (string currency, DateOnly date, ITravelDataProvider p, CancellationToken ct) => p.GetRateAsync(currency, date, ct));
        var auth = app.MapGroup("/api/auth");
        auth.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        auth.MapPost("/register", async (RegisterRequest request, UserManager<IdentityUser> users) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 ||
                !System.Net.Mail.MailAddress.TryCreate(request.Email, out var address) || address.Address != request.Email ||
                string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
                return Results.BadRequest(new ApiError(L.T("Podaj poprawny e-mail i hasło (10–128 znaków, duża i mała litera oraz cyfra).")));
            var user = new IdentityUser { UserName = request.Email, Email = request.Email };
            var result = await users.CreateAsync(user, request.Password);
            return result.Succeeded ? Results.Ok() : Results.BadRequest(new ApiError(
                L.T("Nie udało się utworzyć konta. Sprawdź e-mail i hasło (minimum 10 znaków, duża i mała litera oraz cyfra).")));
        }).RequireRateLimiting("auth");
        auth.MapPost("/login", async (LoginRequest request, SignInManager<IdentityUser> signIn) =>
        {
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
                return Results.BadRequest(new ApiError(L.T("Podaj e-mail i hasło.")));
            var result = await signIn.PasswordSignInAsync(request.Email, request.Password, false, true);
            return result.Succeeded ? Results.Ok() : Results.Json(new ApiError(
                L.T("Nieprawidłowe dane logowania lub konto jest czasowo zablokowane.")), statusCode: 401);
        }).RequireRateLimiting("auth");
        auth.MapPost("/logout", async (SignInManager<IdentityUser> signIn) => { await signIn.SignOutAsync(); return Results.Ok(); });
        auth.MapGet("/me", (ClaimsPrincipal user) =>
            Results.Ok(new UserInfo(UserId(user), user.FindFirstValue(ClaimTypes.Email) ?? user.Identity!.Name!))).RequireAuthorization();

        var trips = app.MapGroup("/api/trips").RequireAuthorization();
        trips.MapGet("/", async (TravelDbContext db, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            var id = UserId(user);
            var records = await db.Trips.AsNoTracking().Include(x => x.Members)
                .Where(x => x.OwnerId == id || x.Members.Any(m => m.UserId == id))
                .OrderByDescending(x => x.UpdatedAt).ToListAsync(cancellationToken);
            return Results.Ok(records.Select(x => Envelope(x, id)));
        });
        trips.MapPut("/{id:guid}", SaveTripAsync);
        trips.MapGet("/{id:guid}/members", async (Guid id, TravelDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == UserId(user), ct);
            if (trip is null) return Results.NotFound();
            var members = await (from member in db.Members join account in db.Users on member.UserId equals account.Id
                                 where member.TripId == id
                                 select new ParticipantInfo(account.Id, account.Email!, member.CanEdit ? ParticipantRole.Editor : ParticipantRole.Viewer)).ToArrayAsync(ct);
            return Results.Ok(members);
        });
        trips.MapPost("/{id:guid}/members", async (Guid id, ShareTripRequest request, TravelDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var trip = await db.Trips.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == UserId(user), ct);
            if (trip is null) return Results.NotFound();
            if (!Enum.IsDefined(request.Role) || string.IsNullOrWhiteSpace(request.Email))
                return Results.BadRequest(new ApiError(L.T("Podaj e-mail i prawidłową rolę.")));
            var normalized = request.Email.Trim().ToUpperInvariant();
            var account = await db.Users.FirstOrDefaultAsync(x => x.NormalizedEmail == normalized, ct);
            if (account is null) return Results.BadRequest(new ApiError(L.T("Uczestnik musi najpierw założyć konto Travelio.")));
            if (account.Id == trip.OwnerId) return Results.BadRequest(new ApiError(L.T("Jesteś już właścicielem tej podróży.")));
            var member = await db.Members.FindAsync([id, account.Id], ct);
            if (member is null) { member = new() { TripId = id, UserId = account.Id }; db.Members.Add(member); }
            member.CanEdit = request.Role == ParticipantRole.Editor;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
        trips.MapDelete("/{id:guid}/members/{memberId}", async (Guid id, string memberId, TravelDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (!await db.Trips.AnyAsync(x => x.Id == id && x.OwnerId == UserId(user), ct)) return Results.NotFound();
            await db.Members.Where(x => x.TripId == id && x.UserId == memberId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
        var passport = app.MapGroup("/api/passport").RequireAuthorization();
        passport.MapGet("/", async (TravelDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var record = await db.Passports.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == UserId(user), ct);
            return Results.Ok(record is null ? new PassportEnvelope([], 0)
                : new PassportEnvelope(JsonSerializer.Deserialize<List<string>>(record.CountriesJson)!, record.Version));
        });
        passport.MapPut("/", async (SavePassportRequest request, TravelDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (request.Countries is null || request.Countries.Count > 250 ||
                request.Countries.Any(x => x is null || x.Length != 2 || x.Any(c => c is < 'A' or > 'Z')))
                return Results.BadRequest(new ApiError(L.T("Nieprawidłowe kody krajów.")));
            var id = UserId(user);
            var record = await db.Passports.FirstOrDefaultAsync(x => x.UserId == id, ct);
            if ((record?.Version ?? 0) != request.ExpectedVersion) return Results.Conflict(new ApiError(L.T("Paszport zmienił się na innym urządzeniu. Spróbuj ponownie.")));
            if (record is null) { record = new() { UserId = id }; db.Passports.Add(record); }
            record.CountriesJson = JsonSerializer.Serialize(request.Countries.Distinct().Order().ToArray());
            record.Version++;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new ApiError(L.T("Wykryto równoczesną zmianę paszportu."))); }
            return Results.Ok(new PassportEnvelope(request.Countries.Distinct().ToList(), record.Version));
        });
        app.MapGet("/api/{**path}", () => Results.NotFound(new ApiError("Nie znaleziono endpointu.")));
    }

    private static async Task<IResult> SaveTripAsync(Guid id, SaveTripRequest request, TravelDbContext db,
        ClaimsPrincipal user, IDestinationCatalog catalog, CancellationToken ct)
    {
        if (request.Trip is null || id != request.Trip.Id || request.ExpectedVersion < 0)
            return Results.BadRequest(new ApiError(L.T("Nieprawidłowy identyfikator lub wersja podróży.")));
        TripValidator.Validate(request.Trip, catalog);
        var owner = UserId(user);
        var record = await db.Trips.Include(x => x.Members).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (record is not null && record.OwnerId != owner && !record.Members.Any(x => x.UserId == owner))
            return Results.NotFound();
        if (record is not null && record.OwnerId != owner &&
            (request.Trip.IsDeleted || !record.Members.Any(x => x.UserId == owner && x.CanEdit))) return Results.Forbid();
        if ((record?.Version ?? 0) != request.ExpectedVersion)
            return record is null ? Results.Conflict() : Results.Conflict(Envelope(record, owner));
        if (record is null)
        {
            record = new() { Id = id, OwnerId = owner };
            db.Trips.Add(record);
        }
        record.Payload = JsonSerializer.Serialize(request.Trip, Json);
        record.Version++;
        record.UpdatedAt = DateTime.UtcNow;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var latest = await db.Trips.AsNoTracking().Include(x => x.Members).SingleOrDefaultAsync(x => x.Id == id, ct);
            return latest is null ? Results.Conflict() : Results.Conflict(Envelope(latest, owner));
        }
        return Results.Ok(Envelope(record, owner));
    }

    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static TripEnvelope Envelope(TripRecord record, string user) =>
        new(JsonSerializer.Deserialize<Trip>(record.Payload, Json)!, record.Version,
            record.OwnerId == user || record.Members.Any(x => x.UserId == user && x.CanEdit), record.OwnerId == user);
}

