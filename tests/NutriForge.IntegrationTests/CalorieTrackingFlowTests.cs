using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace NutriForge.IntegrationTests;

/// <summary>
/// Proves the calorie-tracking MVP actually works at runtime against real Postgres + Redis:
/// profile → derived target → food search → diary log (log-time snapshot) → day rollup → GDPR
/// export. The cross-cutting contract stays on (auth via the dev scheme, per-user isolation,
/// idempotency).
/// </summary>
[Collection("AppHost")]
public sealed class CalorieTrackingFlowTests(AppHostFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Health_endpoint_is_green()
    {
        var client = await fixture.CreateReadyClientAsync();
        using var res = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Full_calorie_tracking_flow_works_end_to_end()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"flow-{Guid.NewGuid():N}");
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // 1. Set the profile (the golden worked example: born 1996-06-23 ⇒ age 30).
        var profile = new
        {
            sex = "Male",
            birthDate = "1996-06-23",
            heightCm = 180.0,
            weightKg = 80.0,
            bodyFatPct = (double?)null,
            activity = "ModeratelyActive",
            goal = "ModerateCut",
            macroStrategy = "ProteinAnchored",
            allergens = new[] { "peanuts" },
            dislikes = Array.Empty<string>(),
            preferredDiets = Array.Empty<string>(),
        };
        using (var put = await client.PutAsJsonAsync("/api/v1/profile", profile, Json))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }

        // 2. The derived target matches the documented tuple.
        var target = await client.GetFromJsonAsync<JsonElement>("/api/v1/targets", Json);
        Assert.Equal(2345, target.GetProperty("kcal").GetDouble());
        Assert.Equal(176, target.GetProperty("proteinG").GetDouble());

        // 3. Search the seeded catalog for an egg and grab a portion.
        var foods = await client.GetFromJsonAsync<JsonElement>("/api/v1/foods/search?q=egg", Json);
        Assert.True(foods.GetArrayLength() > 0, "expected the dev seeder to have an egg");
        var egg = foods[0];
        var foodId = egg.GetProperty("id").GetString();
        var portion = egg.GetProperty("portions").EnumerateArray()
            .First(p => p.GetProperty("name").GetString()!.Contains("egg", StringComparison.OrdinalIgnoreCase));
        var portionId = portion.GetProperty("id").GetString();

        // 4. Log 2 eggs at breakfast (idempotency key present, as a real client would send).
        using var logReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/diary")
        {
            Content = JsonContent.Create(
                new { date = today, mealSlot = "Breakfast", foodId, portionId, quantity = 2.0 }, options: Json),
        };
        logReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using (var log = await client.SendAsync(logReq))
        {
            Assert.Equal(HttpStatusCode.Created, log.StatusCode);
            var entry = await log.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.True(entry.GetProperty("kcal").GetDouble() > 0, "entry should carry a nutrition snapshot");
        }

        // 5. The day rolls up against the target.
        var day = await client.GetFromJsonAsync<JsonElement>($"/api/v1/diary?date={today}", Json);
        Assert.Equal(1, day.GetProperty("entries").GetArrayLength());
        var consumedKcal = day.GetProperty("consumed").GetProperty("kcal").GetDouble();
        Assert.True(consumedKcal > 0, "consumed kcal should reflect the logged egg");
        Assert.Equal(2345 - consumedKcal, day.GetProperty("remaining").GetProperty("kcal").GetDouble(), precision: 1);

        // 6. GDPR export returns the user's bundle including the diary entry.
        var export = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/export", Json);
        Assert.Equal(1, export.GetProperty("diary").GetArrayLength());
        Assert.Equal("ModeratelyActive", export.GetProperty("profile").GetProperty("activity").GetString());
    }

    [Fact]
    public async Task Assistant_endpoint_is_wired_and_degrades_gracefully_without_a_provider()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"ai-{Guid.NewGuid():N}");

        // The agentic surface exists; with no provider configured it reports unconfigured...
        var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/assistant/status", Json);
        Assert.False(status.GetProperty("configured").GetBoolean());

        // ...and a chat attempt degrades to 503 Problem Details rather than failing the request.
        using var chat = new HttpRequestMessage(HttpMethod.Post, "/api/v1/assistant/chat")
        {
            Content = JsonContent.Create(new { message = "How many calories do I have left?" }, options: Json),
        };
        using var res = await client.SendAsync(chat);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected_and_admin_surface_is_gated()
    {
        // A separate client with NO dev-auth headers is anonymous.
        var anon = fixture.App.CreateHttpClient("api");
        using var unauth = await anon.GetAsync("/api/v1/targets");
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

        // A plain user may not reach the admin import surface.
        var user = await fixture.CreateReadyClientAsync(subject: $"user-{Guid.NewGuid():N}", role: "user");
        using var forbidden = await user.GetAsync("/internal/import/status");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // An admin may.
        var admin = await fixture.CreateReadyClientAsync(subject: $"admin-{Guid.NewGuid():N}", role: "admin");
        using var ok = await admin.GetAsync("/internal/import/status");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Diet_plan_generates_to_ready_and_closes_the_loop_into_a_shopping_list()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"diet-{Guid.NewGuid():N}");

        // A profile so there's a calorie target to generate against.
        using (var put = await client.PutAsJsonAsync("/api/v1/profile", new
        {
            sex = "Male",
            birthDate = "1996-06-23",
            heightCm = 180.0,
            weightKg = 80.0,
            bodyFatPct = (double?)null,
            activity = "ModeratelyActive",
            goal = "ModerateCut",
            macroStrategy = "ProteinAnchored",
            allergens = Array.Empty<string>(),
            dislikes = Array.Empty<string>(),
            preferredDiets = Array.Empty<string>(),
        }, Json))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }

        // Async generate (structured intent — no LLM key needed). 202 + a plan id.
        using var gen = new HttpRequestMessage(HttpMethod.Post, "/api/v1/diet-plans")
        {
            Content = JsonContent.Create(new { dietSlug = "high-protein", horizonDays = 3 }, options: Json),
        };
        gen.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var genRes = await client.SendAsync(gen);
        Assert.Equal(HttpStatusCode.Accepted, genRes.StatusCode);
        var planId = (await genRes.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString();

        // Poll until the background worker finishes the plan.
        JsonElement plan = default;
        var status = "Generating";
        for (var i = 0; i < 30 && status == "Generating"; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            plan = await client.GetFromJsonAsync<JsonElement>($"/api/v1/diet-plans/{planId}", Json);
            status = plan.GetProperty("status").GetString()!;
        }

        Assert.True(status == "Ready",
            $"expected Ready; got {status} (message: {(plan.TryGetProperty("message", out var m) ? m.GetString() : "?")})");
        Assert.True(plan.GetProperty("slots").GetArrayLength() > 0, "a ready plan has meal slots");
        Assert.True(plan.GetProperty("achievedKcal").GetDouble() > 0);

        // Accept it.
        using var acc = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/diet-plans/{planId}/accept");
        acc.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using (var accRes = await client.SendAsync(acc))
        {
            Assert.Equal(HttpStatusCode.OK, accRes.StatusCode);
        }

        // Closed loop: accepted plan → one consolidated, aisle-grouped shopping list.
        using var sl = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/diet-plans/{planId}/shopping-list");
        sl.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var slRes = await client.SendAsync(sl);
        Assert.Equal(HttpStatusCode.Created, slRes.StatusCode);
        var list = await slRes.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(list.GetProperty("aisles").GetArrayLength() > 0, "the shopping list groups items by aisle");
    }
}
