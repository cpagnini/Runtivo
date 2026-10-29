using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Runtivo.Api.Tests;

[Collection("api")]
public class IntentTests(ApiFixture api)
{
    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    internal static async Task<List<(Guid Id, string Name, string City)>> PlacesAsync(HttpClient client, string? city = null)
    {
        var json = await (await client.GetAsync("/api/places" + (city is null ? "" : $"?city={city}"))).Content.ReadFromJsonAsync<JsonElement>();
        return json.EnumerateArray().Select(p => (p.GetProperty("id").GetGuid(), p.GetProperty("name").GetString()!, p.GetProperty("city").GetString()!)).ToList();
    }

    /// <summary>Annuncio valido (martedì e giovedì sera, passo 5:30–6:00); i valori si possono sovrascrivere.</summary>
    internal static Dictionary<string, object?> ValidIntent(Guid placeId, object? overrides = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["homePlaceId"] = placeId,
            ["radiusKm"] = 10,
            ["paceMinSecPerKm"] = 330,
            ["paceMaxSecPerKm"] = 360,
            ["distanceMinMeters"] = 8000,
            ["distanceMaxMeters"] = 10000,
            ["runType"] = "Social",
            ["availability"] = new[] { new { day = "Tuesday", band = "Evening" }, new { day = "Thursday", band = "Evening" } },
            ["allowPairs"] = true,
            ["confirmAdult"] = true,
        };
        return Apply(body, overrides);
    }

    internal static Dictionary<string, object?> Apply(Dictionary<string, object?> body, object? overrides)
    {
        if (overrides is not null)
            foreach (var p in overrides.GetType().GetProperties()) body[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = p.GetValue(overrides);
        return body;
    }

    private static Task<HttpResponseMessage> Put(HttpClient c, Dictionary<string, object?> body) => c.PutAsJsonAsync("/api/me/intent", body);

    [SkippableFact]
    public async Task Places_list_has_no_coordinates()
    {
        RequireDb();
        var raw = await (await api.NewClient().GetAsync("/api/places")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("latitude", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", raw, StringComparison.OrdinalIgnoreCase);
        var places = await PlacesAsync(api.NewClient());
        Assert.Contains(places, p => p.City == "Prato");
        Assert.Contains(places, p => p.City == "Firenze");
        Assert.All(await PlacesAsync(api.NewClient(), "Prato"), p => Assert.Equal("Prato", p.City));
    }

    [SkippableFact]
    public async Task Publishing_requires_login()
    {
        RequireDb();
        var places = await PlacesAsync(api.NewClient());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Put(api.NewClient(), ValidIntent(places[0].Id))).StatusCode);
    }

    [SkippableFact]
    public async Task Publishing_requires_the_adult_confirmation()
    {
        RequireDb();
        var (client, _, _) = await api.LoginAsync();
        var place = (await PlacesAsync(client))[0].Id;

        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, ValidIntent(place, new { ConfirmAdult = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put(client, ValidIntent(place))).StatusCode);
        // Già confermato: non va richiesto di nuovo a ogni modifica.
        Assert.Equal(HttpStatusCode.OK, (await Put(client, ValidIntent(place, new { ConfirmAdult = (bool?)null }))).StatusCode);
    }

    [SkippableFact]
    public async Task Invalid_input_is_rejected()
    {
        RequireDb();
        var (client, _, _) = await api.LoginAsync();
        var place = (await PlacesAsync(client))[0].Id;

        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, ValidIntent(place, new { RadiusKm = 4 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, ValidIntent(place, new { RadiusKm = 31 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, ValidIntent(place, new { PaceMinSecPerKm = 400, PaceMaxSecPerKm = 300 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, ValidIntent(place, new { PaceMinSecPerKm = 60 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, ValidIntent(place, new { DistanceMinMeters = 500 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, ValidIntent(place, new { DistanceMinMeters = 9000, DistanceMaxMeters = 8000 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, ValidIntent(place, new { Availability = Array.Empty<object>() }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, ValidIntent(place, new { HomePlaceId = Guid.NewGuid() }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, ValidIntent(place, new { ExpiresInDays = 91 }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/me/intent")).StatusCode);
    }

    [SkippableFact]
    public async Task Put_replaces_the_existing_intent()
    {
        RequireDb();
        var (client, _, _) = await api.LoginAsync();
        var place = (await PlacesAsync(client))[0].Id;

        await Put(client, ValidIntent(place));
        await Put(client, ValidIntent(place, new { RadiusKm = 25, RunType = "Easy" }));

        var intent = await (await client.GetAsync("/api/me/intent")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(25, intent.GetProperty("radiusKm").GetInt32());
        Assert.Equal("Easy", intent.GetProperty("runType").GetString());
        Assert.Equal(2, intent.GetProperty("availability").GetArrayLength());
        Assert.Equal("Active", intent.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Pause_and_resume_change_the_status()
    {
        RequireDb();
        var (client, _, _) = await api.LoginAsync();
        await Put(client, ValidIntent((await PlacesAsync(client))[0].Id));

        await client.PostAsync("/api/me/intent/pause", null);
        Assert.Equal("Paused", (await (await client.GetAsync("/api/me/intent")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        await client.PostAsync("/api/me/intent/resume", null);
        Assert.Equal("Active", (await (await client.GetAsync("/api/me/intent")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Resume_of_an_expired_intent_extends_it()
    {
        RequireDb();
        var (client, _, _) = await api.LoginAsync();
        await Put(client, ValidIntent((await PlacesAsync(client))[0].Id, new { ExpiresInDays = 1 }));
        api.Time.Advance(TimeSpan.FromDays(3));

        var resumed = await client.PostAsync("/api/me/intent/resume", null);

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        var expires = (await resumed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiresAt").GetDateTimeOffset();
        Assert.True(expires > api.Time.GetUtcNow().AddDays(25));
    }

    [SkippableFact]
    public async Task Delete_removes_the_intent()
    {
        RequireDb();
        var (client, _, _) = await api.LoginAsync();
        await Put(client, ValidIntent((await PlacesAsync(client))[0].Id));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/me/intent")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/me/intent")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/me/intent")).StatusCode);
    }
}
