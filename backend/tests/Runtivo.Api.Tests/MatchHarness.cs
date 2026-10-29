using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Runtivo.Api.Tests;

public record Runner(HttpClient Client, string Email, Guid UserId, string DisplayName);

/// <summary>
/// Crea persone con un annuncio valido. Ogni test usa una "nicchia" (fascia oraria + città) tutta sua: gli orari in comune
/// e il raggio sono filtri obbligatori, quindi i candidati di un test non compaiono negli elenchi degli altri.
/// Le fasce usate sono tutte tranne martedì e giovedì sera, che usano gli <see cref="IntentTests"/>.
/// </summary>
public sealed class MatchHarness(ApiFixture api)
{
    private static readonly (string Day, string Band)[] Niches =
        new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }
            .SelectMany(d => new[] { "Morning", "Midday", "Evening" }.Select(b => (d, b)))
            .Where(s => !(s.d is "Tuesday" or "Thursday" && s.b == "Evening"))
            .ToArray();
    private static int counter;

    private readonly int index = Interlocked.Increment(ref counter) - 1;

    public (string Day, string Band) Niche => Niches[index % Niches.Length];

    /// <summary>Prato e Firenze distano circa 16 km: con raggio 10 non si incontrano mai.</summary>
    public string DefaultPlace => (index / Niches.Length) % 2 == 0 ? "Piazza del Comune" : "Piazza Santa Croce";

    public async Task<Guid> PlaceAsync(string name) =>
        (await IntentTests.PlacesAsync(api.NewClient())).First(p => p.Name == name).Id;

    public async Task<Runner> RunnerAsync(string? place = null, object? overrides = null)
    {
        var (client, email, id) = await api.LoginAsync();
        var name = "Runner " + Guid.NewGuid().ToString("N")[..6];
        (await client.PatchAsJsonAsync("/api/me", new { displayName = name })).EnsureSuccessStatusCode();

        var body = IntentTests.ValidIntent(await PlaceAsync(place ?? DefaultPlace));
        body["availability"] = new[] { new { day = Niche.Day, band = Niche.Band } };
        var response = await client.PutAsJsonAsync("/api/me/intent", IntentTests.Apply(body, overrides));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new Runner(client, email, id, name);
    }

    public static async Task<List<JsonElement>> MatchesAsync(HttpClient c, string query = "")
    {
        var response = await c.GetAsync("/api/me/matches" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    public static bool Contains(IEnumerable<JsonElement> matches, Guid userId) => matches.Any(m => m.GetProperty("userId").GetGuid() == userId);

    public static JsonElement Card(IEnumerable<JsonElement> matches, Guid userId) => matches.First(m => m.GetProperty("userId").GetGuid() == userId);
}
