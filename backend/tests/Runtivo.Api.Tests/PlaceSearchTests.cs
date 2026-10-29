using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runtivo.Api.Matching;

namespace Runtivo.Api.Tests;

public class FakePhotonClient : IPhotonClient
{
    public List<PlaceCandidate> Results { get; set; } = [];
    public int Calls { get; private set; }

    public Task<List<PlaceCandidate>> SearchAsync(string query, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Results);
    }
}

/// <summary>Ricerca del punto di partenza (con Photon simulato) e frase libera dell'annuncio.</summary>
[Collection("api")]
public class PlaceSearchTests : IDisposable
{
    private readonly FakePhotonClient fake = new();
    private readonly ApiFixture app;

    public PlaceSearchTests()
    {
        app = ApiFixture.WithSettings(new Dictionary<string, string>(), s =>
        {
            s.RemoveAll<IPhotonClient>();
            s.AddSingleton<IPhotonClient>(fake);
        });
    }

    public void Dispose() => app.Dispose();

    private void RequireDb() => Skip.IfNot(app.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private async Task<(string Ref, string Name)> SearchOneAsync(HttpClient c, PlaceCandidate candidate)
    {
        fake.Results = [candidate];
        var json = await (await c.GetAsync("/api/places/search?q=Cascine")).Content.ReadFromJsonAsync<JsonElement>();
        var first = json.EnumerateArray().Single();
        return (first.GetProperty("ref").GetString()!, first.GetProperty("name").GetString()!);
    }

    private static async Task<Guid> ResolveAsync(HttpClient c, string reference) =>
        (await (await c.PostAsJsonAsync("/api/places/resolve", new { @ref = reference })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    // ---------- Filtro dei risultati di Photon (senza rete né database) ----------

    private static JsonElement Features(params object[] features) => JsonSerializer.SerializeToElement(new { features });

    private static object Feature(string key, string value, string name, string? city = "Prato", string country = "IT", string? housenumber = null,
        string? type = null, double lon = 11.0963, double lat = 43.8790)
    {
        var properties = new Dictionary<string, string?>
        {
            ["osm_key"] = key, ["osm_value"] = value, ["name"] = name, ["city"] = city, ["housenumber"] = housenumber, ["type"] = type, ["countrycode"] = country,
        };
        return new
        {
            geometry = new { coordinates = new[] { lon, lat } },
            properties = properties.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
    }

    [Fact]
    public void Parse_keeps_public_places_and_localities_and_drops_addresses_and_foreign_results()
    {
        var found = PhotonClient.Parse(Features(
            Feature("leisure", "park", "Parco delle Cascine", "Firenze"),
            Feature("place", "town", "Prato", null),
            Feature("railway", "station", "Firenze Santa Maria Novella", "Firenze"),
            Feature("place", "house", "Via Rossi", "Prato", housenumber: "12"),
            Feature("highway", "residential", "Via Rossi", "Prato"),
            Feature("building", "yes", "Casa mia", "Prato"),
            Feature("leisure", "park", "Parc de la Villette", "Paris", country: "FR"),
            Feature("leisure", "park", "Senza città", null)), ["IT"]);

        Assert.Equal(["Parco delle Cascine", "Prato", "Firenze Santa Maria Novella"], found.Select(f => f.Name).ToArray());
        Assert.Equal("Prato", found.Single(f => f.Name == "Prato").City); // una città è la propria città
    }

    [Fact]
    public void Parse_uses_the_municipality_as_city_and_the_name_only_when_there_is_none()
    {
        var found = PhotonClient.Parse(Features(
            Feature("place", "village", "Cascine di Buti", "Buti"),
            Feature("place", "city", "Firenze", null)), ["IT"]);

        Assert.Equal("Buti", found.Single(f => f.Name == "Cascine di Buti").City);
        Assert.Equal("Firenze", found.Single(f => f.Name == "Firenze").City);
    }

    [Fact]
    public async Task The_request_to_Photon_never_asks_for_an_unsupported_language_and_a_400_gives_no_results()
    {
        var handler = new RecordingHandler(System.Net.HttpStatusCode.BadRequest);
        var client = new PhotonClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new PhotonOptions()));

        var found = await client.SearchAsync("Parco delle Cascine", CancellationToken.None);

        Assert.Empty(found);
        Assert.DoesNotContain("lang=", handler.Url);
        Assert.Contains("q=Parco%20delle%20Cascine", handler.Url);
    }

    private class RecordingHandler(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        public string Url { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    [Fact]
    public void Parse_rounds_the_position_to_about_half_a_kilometre_and_removes_duplicates()
    {
        var found = PhotonClient.Parse(Features(
            Feature("leisure", "park", "Parco", "Prato", lon: 11.096312, lat: 43.879061),
            Feature("leisure", "park", "Parco", "Prato", lon: 11.2, lat: 43.9)), ["IT"]);

        var park = Assert.Single(found);
        Assert.Equal(43.88, park.Latitude);
        Assert.Equal(11.095, park.Longitude);
    }

    [Fact]
    public void Parse_ignores_malformed_responses()
    {
        Assert.Empty(PhotonClient.Parse(JsonDocument.Parse("{}").RootElement, ["IT"]));
        Assert.Empty(PhotonClient.Parse(JsonDocument.Parse("""{"features":[{"properties":{}}]}""").RootElement, ["IT"]));
    }

    // ---------- Endpoint ----------

    [SkippableFact]
    public async Task Search_requires_login_and_ignores_very_short_text()
    {
        RequireDb();
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewClient().GetAsync("/api/places/search?q=Cascine")).StatusCode);

        var (client, _, _) = await app.LoginNamedAsync();
        fake.Results = [new PlaceCandidate("Parco", "Prato", 43.88, 11.095)];
        var json = await (await client.GetAsync("/api/places/search?q=ab")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(json.EnumerateArray());
        Assert.Equal(0, fake.Calls); // sotto le tre lettere non si interroga il servizio esterno
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/places/search?q=" + new string('a', 81))).StatusCode);
    }

    [SkippableFact]
    public async Task Search_results_carry_a_signed_reference_and_never_expose_coordinates()
    {
        RequireDb();
        var (client, _, _) = await app.LoginNamedAsync();
        fake.Results = [new PlaceCandidate("Parco Test " + Unique(), "Prato", 43.88, 11.095)];

        var raw = await (await client.GetAsync("/api/places/search?q=Parco")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("43.88", raw);
        Assert.DoesNotContain("latitude", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"ref\"", raw);
    }

    [SkippableFact]
    public async Task Resolve_creates_a_place_reuses_it_and_keeps_it_out_of_the_meeting_point_list()
    {
        RequireDb();
        var (client, _, _) = await app.LoginNamedAsync();
        var name = "Parco Test " + Unique();
        var (reference, _) = await SearchOneAsync(client, new PlaceCandidate(name, "Prato", 43.88, 11.095));

        var first = await ResolveAsync(client, reference);
        var second = await ResolveAsync(client, reference);

        Assert.Equal(first, second); // niente duplicati se lo scelgono in due (o due volte)
        Assert.DoesNotContain(await IntentTests.PlacesAsync(client), p => p.Id == first); // non diventa un punto di ritrovo
    }

    [SkippableFact]
    public async Task Resolve_rejects_invented_tampered_or_missing_references()
    {
        RequireDb();
        var (client, _, _) = await app.LoginNamedAsync();
        var (reference, _) = await SearchOneAsync(client, new PlaceCandidate("Parco Test " + Unique(), "Prato", 43.88, 11.095));

        foreach (var bad in new[] { "", "non-valido", reference[..^4] + "AAAA", Convert.ToBase64String("{\"n\":\"Casa\",\"c\":\"Prato\",\"la\":43.8,\"lo\":11.0}"u8.ToArray()) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/places/resolve", new { @ref = bad })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewClient().PostAsJsonAsync("/api/places/resolve", new { @ref = reference })).StatusCode);
    }

    [SkippableFact]
    public async Task An_intent_can_start_from_a_searched_place_and_shows_only_name_and_city_to_the_owner()
    {
        RequireDb();
        var (client, _, _) = await app.LoginNamedAsync();
        var name = "Parco Test " + Unique();
        var (reference, _) = await SearchOneAsync(client, new PlaceCandidate(name, "Prato", 43.88, 11.095));
        var placeId = await ResolveAsync(client, reference);

        var response = await client.PutAsJsonAsync("/api/me/intent", IntentTests.ValidIntent(placeId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(name, body);
        Assert.DoesNotContain("latitude", body, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task A_searched_place_cannot_be_used_as_the_meeting_point_of_a_proposed_run()
    {
        RequireDb();
        var (client, _, _) = await app.LoginNamedAsync();
        var (reference, _) = await SearchOneAsync(client, new PlaceCandidate("Parco Test " + Unique(), "Prato", 43.88, 11.095));
        var placeId = await ResolveAsync(client, reference);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/me/intent", IntentTests.ValidIntent(placeId))).StatusCode);
        var (_, _, otherId) = await app.LoginNamedAsync();

        var response = await client.PostAsJsonAsync("/api/runs", new
        {
            placeId,
            startsAt = app.Time.GetUtcNow().AddDays(2),
            distanceMeters = 8000,
            inviteeIds = new[] { otherId },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("luogo dall'elenco", await response.Content.ReadAsStringAsync());
    }

    // ---------- Frase libera ----------

    private async Task<HttpResponseMessage> PublishWithNoteAsync(HttpClient c, string? note)
    {
        var place = (await IntentTests.PlacesAsync(c, "Prato")).First();
        return await c.PutAsJsonAsync("/api/me/intent", IntentTests.ValidIntent(place.Id, new { Note = note }));
    }

    [SkippableFact]
    public async Task The_note_is_trimmed_saved_and_shown_to_compatible_people()
    {
        RequireDb();
        var (a, _, _) = await app.LoginNamedAsync();
        var (b, _, bId) = await app.LoginNamedAsync();
        Assert.Equal(HttpStatusCode.OK, (await PublishWithNoteAsync(b, "  Cerco chi corre\n piano   e chiacchiera.  ")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PublishWithNoteAsync(a, null)).StatusCode);

        var matches = await (await a.GetAsync("/api/me/matches?take=50")).Content.ReadFromJsonAsync<JsonElement>();

        var theirs = matches.EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == bId);
        Assert.Equal("Cerco chi corre piano e chiacchiera.", theirs.GetProperty("note").GetString());
        var mine = await (await b.GetAsync("/api/me/intent")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cerco chi corre piano e chiacchiera.", mine.GetProperty("note").GetString());
    }

    [SkippableTheory]
    [InlineData("Scrivimi a mario@example.com")]
    [InlineData("Chiamami al 333 123 4567")]
    [InlineData("Chiamami 3331234567")]
    [InlineData("Guarda su www.mio-sito.it")]
    [InlineData("vedi https://esempio.org/x")]
    [InlineData("Ci sentiamo su WhatsApp")]
    [InlineData("Sono su Instagram")]
    public async Task The_note_refuses_contacts_and_invitations_to_other_apps(string note)
    {
        RequireDb();
        var (client, _, _) = await app.LoginNamedAsync();

        var response = await PublishWithNoteAsync(client, note);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sicurezza", await response.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task The_note_has_a_length_limit_and_can_be_cleared()
    {
        RequireDb();
        var (client, _, _) = await app.LoginNamedAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await PublishWithNoteAsync(client, new string('a', NoteRules.MaxLength + 1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PublishWithNoteAsync(client, new string('a', NoteRules.MaxLength))).StatusCode);
        var cleared = await (await PublishWithNoteAsync(client, "   ")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("note").ValueKind);
    }

    [Theory]
    [InlineData("Corro piano, 6 km il martedì", true)]
    [InlineData("Alle 19:30 al parco, 10 km", true)]
    [InlineData("Dal 2024 corro 3 volte a settimana", true)]
    [InlineData("scrivimi@qui", false)]
    public void NoteRules_accepts_normal_presentations_and_not_contacts(string text, bool accepted)
    {
        var (note, error) = NoteRules.Check(text);
        Assert.Equal(accepted, error is null && note is not null);
    }
}
