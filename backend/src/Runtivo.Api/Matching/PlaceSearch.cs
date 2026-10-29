using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Runtivo.Api.Common;
using Runtivo.Api.Data;

namespace Runtivo.Api.Matching;

public class PhotonOptions
{
    public string BaseUrl { get; set; } = "https://photon.komoot.io";
    /// <summary>Paesi ammessi (codici ISO). Vuoto = nessun filtro.</summary>
    public string[] CountryCodes { get; set; } = ["IT"];
    /// <summary>Punto verso cui orientare i risultati: favorisce, non limita.</summary>
    public double BiasLatitude { get; set; } = 43.80;
    public double BiasLongitude { get; set; } = 11.15;
}

/// <summary>Un luogo pubblico proposto dalla ricerca, già ripulito: niente indirizzi civici, coordinate già arrotondate.</summary>
public record PlaceCandidate(string Name, string City, double Latitude, double Longitude);

public interface IPhotonClient
{
    /// <summary>Luoghi che corrispondono al testo; lista vuota se il servizio non risponde.</summary>
    Task<List<PlaceCandidate>> SearchAsync(string query, CancellationToken ct);
}

/// <summary>
/// Ricerca sui dati di OpenStreetMap tramite Photon. Del testo digitato arriva al servizio solo ciò che la persona scrive nel campo;
/// dei risultati si tengono soltanto luoghi pubblici e località, mai indirizzi, e la posizione si arrotonda (~500 m).
/// </summary>
public class PhotonClient(HttpClient http, IOptions<PhotonOptions> options) : IPhotonClient
{
    public async Task<List<PlaceCandidate>> SearchAsync(string query, CancellationToken ct)
    {
        var o = options.Value;
        // Nessun parametro "lang": Photon accetta solo default, de, en e fr, e con altri valori risponde 400. Il default dà i nomi locali.
        var url = string.Create(CultureInfo.InvariantCulture,
            $"{o.BaseUrl.TrimEnd('/')}/api/?q={Uri.EscapeDataString(query)}&limit=30&lat={o.BiasLatitude}&lon={o.BiasLongitude}");
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return [];
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return Parse(body.RootElement, o.CountryCodes);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return [];
        }
    }

    private static readonly HashSet<string> Localities = ["city", "town", "village", "suburb", "neighbourhood", "quarter", "hamlet", "borough", "square"];
    private static readonly HashSet<string> Leisure = ["park", "sports_centre", "stadium", "track", "pitch", "recreation_ground", "garden", "nature_reserve", "playground"];

    public static List<PlaceCandidate> Parse(JsonElement root, string[] countryCodes)
    {
        var result = new List<PlaceCandidate>();
        if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array) return result;

        foreach (var f in features.EnumerateArray())
        {
            if (!f.TryGetProperty("properties", out var p) || !f.TryGetProperty("geometry", out var g)) continue;
            if (!g.TryGetProperty("coordinates", out var c) || c.ValueKind != JsonValueKind.Array || c.GetArrayLength() < 2) continue;

            string? Str(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
            var key = Str("osm_key");
            var value = Str("osm_value");
            var name = Str("name");
            if (string.IsNullOrEmpty(name) || key is null || value is null) continue;
            // Un indirizzo civico non è un luogo pubblico: non si propone mai.
            if (Str("housenumber") is not null || Str("type") is "house" or "street") continue;
            if (countryCodes.Length > 0 && !countryCodes.Contains(Str("countrycode") ?? "", StringComparer.OrdinalIgnoreCase)) continue;

            var isLocality = key == "place" && Localities.Contains(value);
            var allowed = isLocality
                          || (key == "leisure" && Leisure.Contains(value))
                          || (key == "railway" && value == "station")
                          || (key == "tourism" && value is "attraction" or "viewpoint");
            if (!allowed) continue;

            // «city» di Photon è il comune; per una città o un paese privi del campo vale il nome stesso.
            var isSettlement = isLocality && value is "city" or "town" or "village";
            var city = Str("city") ?? (isSettlement ? name : null) ?? Str("county") ?? Str("state") ?? "";
            if (city.Length == 0) continue;
            if (!c[0].TryGetDouble(out var lon) || !c[1].TryGetDouble(out var lat)) continue;

            var rounded = new PlaceCandidate(name.Length > 100 ? name[..100] : name, city.Length > 60 ? city[..60] : city, PlaceGrid.Round(lat), PlaceGrid.Round(lon));
            if (!result.Any(r => r.Name == rounded.Name && r.City == rounded.City)) result.Add(rounded);
        }
        return result;
    }
}

public static class PlaceGrid
{
    private const double Step = 0.005; // ~550 m in latitudine

    /// <summary>Porta la coordinata su una griglia di circa 500 m: non si conserva mai un punto più preciso.</summary>
    public static double Round(double degrees) => Math.Round(Math.Round(degrees / Step) * Step, 3);
}

public record PlaceSuggestionDto(string Ref, string Name, string City);
public record ResolvePlaceDto(string? Ref);

public static class PlaceSearchEndpoints
{
    private const string Purpose = "runtivo.places";
    private static readonly TimeSpan RefLifetime = TimeSpan.FromMinutes(30);
    private const int MaxResults = 8;

    private record Payload(string N, string C, double La, double Lo);

    public static void MapPlaceSearchEndpoints(this WebApplication app)
    {
        var places = app.MapGroup("/api/places").RequireAuthorization().RequireRateLimiting("search");

        // Il testo cercato non si registra nei log; i risultati portano un riferimento firmato, così nessuno può inventare luoghi o coordinate.
        places.MapGet("/search", async (string? q, IPhotonClient photon, IDataProtectionProvider protection, CancellationToken ct) =>
        {
            var text = q?.Trim() ?? "";
            if (text.Length < 3) return Results.Ok(Array.Empty<PlaceSuggestionDto>());
            if (text.Length > 80) return Http.Problem(400, "Il testo da cercare è troppo lungo.");

            var protector = protection.CreateProtector(Purpose).ToTimeLimitedDataProtector();
            var found = await photon.SearchAsync(text, ct);
            return Results.Ok(found.Take(MaxResults).Select(c => new PlaceSuggestionDto(
                protector.Protect(JsonSerializer.Serialize(new Payload(c.Name, c.City, c.Latitude, c.Longitude)), RefLifetime), c.Name, c.City)));
        });

        // Il luogo scelto diventa un punto di partenza utilizzabile nell'annuncio (non compare nell'elenco dei punti di ritrovo).
        places.MapPost("/resolve", async (ResolvePlaceDto dto, IDataProtectionProvider protection, RuntivoDbContext db, CancellationToken ct) =>
        {
            Payload? payload;
            try
            {
                payload = string.IsNullOrEmpty(dto.Ref) ? null
                    : JsonSerializer.Deserialize<Payload>(protection.CreateProtector(Purpose).ToTimeLimitedDataProtector().Unprotect(dto.Ref));
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
            {
                payload = null;
            }
            if (payload is null) return Http.Problem(400, "La ricerca è scaduta: cerca di nuovo il luogo.");

            // Stesse coordinate arrotondate e stesso nome: si riusa il luogo già creato da qualcun altro.
            var place = await db.PublicPlaces.FirstOrDefaultAsync(
                p => p.City == payload.C && p.Name == payload.N && p.Latitude == payload.La && p.Longitude == payload.Lo, ct);
            if (place is null)
            {
                place = new PublicPlace { Id = Guid.NewGuid(), Name = payload.N, City = payload.C, Latitude = payload.La, Longitude = payload.Lo, IsCurated = false };
                db.PublicPlaces.Add(place);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                    place = await db.PublicPlaces.FirstAsync(p => p.City == payload.C && p.Name == payload.N && p.Latitude == payload.La && p.Longitude == payload.Lo, ct);
                }
            }
            return place.IsActive ? Results.Ok(new PlaceDto(place.Id, place.Name, place.City)) : Http.Problem(400, "Questo luogo non è disponibile.");
        });
    }
}
