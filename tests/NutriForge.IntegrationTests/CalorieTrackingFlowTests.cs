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

    [Fact]
    public async Task Concurrent_first_requests_for_a_new_subject_do_not_500_on_the_provisioning_race()
    {
        // Make sure the app is serving first (this provisions a *different*, warm-up subject).
        await fixture.CreateReadyClientAsync(subject: $"warmup-{Guid.NewGuid():N}");

        // A brand-new subject that has never been provisioned. Build the client directly so the
        // burst below is its very first contact with the API — exactly what a browser does when it
        // fires several calls in parallel on page load. Pre-fix, the losers of the insert race hit
        // the unique OidcSubject constraint (Postgres 23505) and returned 500.
        var subject = $"race-{Guid.NewGuid():N}";

        // Each request gets its OWN client → its own connection. A single shared client would put
        // every request on one multiplexed HTTP/2 connection, letting the first request commit
        // provisioning before the others even reach the server — hiding the race. Separate
        // connections, released together, make the check-then-insert window observable.
        const int Burst = 24;
        var clients = Enumerable.Range(0, Burst).Select(_ =>
        {
            var c = fixture.App.CreateHttpClient("api");
            c.Timeout = TimeSpan.FromSeconds(100);
            c.DefaultRequestHeaders.Add("X-Debug-Subject", subject);
            c.DefaultRequestHeaders.Add("X-Debug-Role", "user");
            return c;
        }).ToArray();

        try
        {
            var start = new TaskCompletionSource();
            var tasks = clients.Select(async c =>
            {
                await start.Task;
                return await c.GetAsync("/api/v1/me/export");
            }).ToArray();
            start.SetResult();
            var responses = await Task.WhenAll(tasks);

            try
            {
                // Pre-fix, the losers of the insert race returned 500 (Postgres 23505).
                Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            }
            finally
            {
                foreach (var r in responses)
                {
                    r.Dispose();
                }
            }

            // The race converged to a single, stable identity: repeated reads return the same row.
            var first = (await clients[0].GetFromJsonAsync<JsonElement>("/api/v1/me/export", Json)).GetProperty("user").GetRawText();
            var second = (await clients[0].GetFromJsonAsync<JsonElement>("/api/v1/me/export", Json)).GetProperty("user").GetRawText();
            Assert.Equal(first, second);
        }
        finally
        {
            foreach (var c in clients)
            {
                c.Dispose();
            }
        }
    }

    // -------------------- Multi-person batch cooking (Eaters multiplier) --------------------

    /// <summary>The load-bearing invariant: cooking for N people must NOT change the per-eater plan.</summary>
    [Fact]
    public async Task Eaters_multiplier_does_not_leak_into_the_per_eater_plan()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"eaters-inv-{Guid.NewGuid():N}");
        var one = await CreateReadyPlanAsync(client, eaters: 1);
        var two = await CreateReadyPlanAsync(client, eaters: 2);

        Assert.Equal(1, one.GetProperty("eaters").GetInt32());
        Assert.Equal(2, two.GetProperty("eaters").GetInt32());

        // Target + achieved daily averages are per-eater and must be identical regardless of headcount.
        Assert.Equal(one.GetProperty("targetKcal").GetDouble(), two.GetProperty("targetKcal").GetDouble());
        Assert.Equal(one.GetProperty("achievedKcal").GetDouble(), two.GetProperty("achievedKcal").GetDouble());
        Assert.Equal(one.GetProperty("achievedProteinG").GetDouble(), two.GetProperty("achievedProteinG").GetDouble());
        Assert.Equal(one.GetProperty("achievedFatG").GetDouble(), two.GetProperty("achievedFatG").GetDouble());
        Assert.Equal(one.GetProperty("achievedCarbG").GetDouble(), two.GetProperty("achievedCarbG").GetDouble());

        // Every slot's per-eater servings + macros are identical too (generation never saw Eaters).
        var s1 = one.GetProperty("slots");
        var s2 = two.GetProperty("slots");
        Assert.Equal(s1.GetArrayLength(), s2.GetArrayLength());
        for (var i = 0; i < s1.GetArrayLength(); i++)
        {
            Assert.Equal(s1[i].GetProperty("servings").GetDouble(), s2[i].GetProperty("servings").GetDouble());
            Assert.Equal(s1[i].GetProperty("kcal").GetDouble(), s2[i].GetProperty("kcal").GetDouble());
        }
    }

    /// <summary>Eaters=2 buys exactly 2x the groceries (fresh subject ⇒ empty pantry, no subtraction).</summary>
    [Fact]
    public async Task Eaters_scales_the_shopping_list_by_the_people_count()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"eaters-shop-{Guid.NewGuid():N}");
        var one = await CreateReadyPlanAsync(client, eaters: 1);
        var two = await CreateReadyPlanAsync(client, eaters: 2);

        var gramsOne = await ShoppingGramsTotalAsync(client, one.GetProperty("id").GetString()!);
        var gramsTwo = await ShoppingGramsTotalAsync(client, two.GetProperty("id").GetString()!);

        Assert.True(gramsOne > 0, "expected a non-empty shopping list");
        Assert.InRange(gramsTwo / gramsOne, 1.98, 2.02); // 2 people ⇒ ~2x groceries (pantry empty)
    }

    /// <summary>Omitted/zero eaters clamps to 1 (back-compat); a real value round-trips.</summary>
    [Fact]
    public async Task Eaters_defaults_and_clamps_and_round_trips()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"eaters-clamp-{Guid.NewGuid():N}");

        var omitted = await CreateReadyPlanAsync(client, eaters: null);
        Assert.Equal(1, omitted.GetProperty("eaters").GetInt32());

        var zero = await CreateReadyPlanAsync(client, eaters: 0);
        Assert.Equal(1, zero.GetProperty("eaters").GetInt32());

        var two = await CreateReadyPlanAsync(client, eaters: 2);
        Assert.Equal(2, two.GetProperty("eaters").GetInt32());
    }

    /// <summary>The on-demand daily summary sends to the (default) mock channel and lands in the outbox.</summary>
    [Fact]
    public async Task Daily_summary_sends_to_the_mock_channel_outbox()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"notif-{Guid.NewGuid():N}");

        using var post = new HttpRequestMessage(HttpMethod.Post, "/api/v1/notifications/daily-summary");
        using var res = await client.SendAsync(post);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var result = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(result.GetProperty("sent").GetBoolean());
        Assert.Equal("mock", result.GetProperty("channel").GetString());
        var message = result.GetProperty("message").GetString();
        Assert.False(string.IsNullOrWhiteSpace(message));

        var outbox = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/outbox", Json);
        Assert.True(outbox.GetArrayLength() >= 1, "expected the sent summary in the outbox");
        Assert.Equal("mock", outbox[0].GetProperty("channel").GetString());
        Assert.Equal(message, outbox[0].GetProperty("body").GetString());
    }

    /// <summary>The meal-prep PDF export renders a real PDF for a ready plan.</summary>
    [Fact]
    public async Task Diet_plan_pdf_export_returns_a_valid_pdf()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"pdf-{Guid.NewGuid():N}");
        var plan = await CreateReadyPlanAsync(client, eaters: 2);

        using var res = await client.GetAsync($"/api/v1/diet-plans/{plan.GetProperty("id").GetString()}/pdf");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/pdf", res.Content.Headers.ContentType?.MediaType);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 500, "expected a non-trivial PDF");
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4)); // PDF magic number
    }

    /// <summary>Recipe import is wired and degrades to 503 when no AI provider is configured.</summary>
    [Fact]
    public async Task Recipe_import_preview_degrades_to_503_without_a_provider()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"import-{Guid.NewGuid():N}");
        using var res = await client.PostAsJsonAsync("/api/v1/recipes/import/preview",
            new { url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ" }, Json);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    /// <summary>An imported recipe persists its source fields, and re-importing the same video dedups.</summary>
    [Fact]
    public async Task Imported_recipe_carries_source_fields_and_dedups_by_video_id()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"recipe-src-{Guid.NewGuid():N}");
        var videoId = Guid.NewGuid().ToString("N")[..11]; // unique 11-char id

        object body = new
        {
            name = "Test imported recipe",
            servings = 2,
            totalMinutes = 10,
            instructions = "mix it",
            tags = new[] { "test" },
            ingredients = new[] { new { quantity = 100.0, unit = "g", name = "egg" } },
            sourceUrl = $"https://www.youtube.com/watch?v={videoId}",
            sourceType = "youtube",
            sourceVideoId = videoId,
            thumbnailUrl = "https://example.com/thumb.jpg",
        };

        using var first = await client.PostAsJsonAsync("/api/v1/recipes", body, Json);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id1 = created.GetProperty("id").GetString();
        Assert.Equal(videoId, created.GetProperty("sourceVideoId").GetString());
        Assert.Equal("youtube", created.GetProperty("sourceType").GetString());

        // Re-importing the same video returns the SAME recipe (no duplicate in the public catalog).
        using var second = await client.PostAsJsonAsync("/api/v1/recipes", body, Json);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var created2 = await second.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(id1, created2.GetProperty("id").GetString());
    }

    /// <summary>Photo logging is wired and degrades to 503 when no vision provider is configured.</summary>
    [Fact]
    public async Task Photo_parse_degrades_gracefully_without_a_vision_provider()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"photo-{Guid.NewGuid():N}");

        using var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }); // tiny fake JPEG
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(image, "image", "meal.jpg");

        using var res = await client.PostAsync("/api/v1/diary/parse-photo", content);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    /// <summary>Create a diet plan (fresh idempotency key) and poll until it is Ready.</summary>
    private static async Task<JsonElement> CreateReadyPlanAsync(HttpClient client, int? eaters, int days = 3)
    {
        object body = eaters is null
            ? new { dietSlug = "high-protein", horizonDays = days, kcalTarget = 2000.0 }
            : new { dietSlug = "high-protein", horizonDays = days, kcalTarget = 2000.0, eaters };
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/diet-plans")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); // distinct creates ⇒ distinct keys
        using var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var planId = (await res.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString();

        JsonElement plan = default;
        var status = "Generating";
        for (var i = 0; i < 30 && status == "Generating"; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            plan = await client.GetFromJsonAsync<JsonElement>($"/api/v1/diet-plans/{planId}", Json);
            status = plan.GetProperty("status").GetString()!;
        }

        Assert.Equal("Ready", status);
        return plan;
    }

    private static async Task<double> ShoppingGramsTotalAsync(HttpClient client, string planId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/diet-plans/{planId}/shopping-list");
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var list = await res.Content.ReadFromJsonAsync<JsonElement>(Json);

        double total = 0;
        foreach (var aisle in list.GetProperty("aisles").EnumerateArray())
        {
            foreach (var item in aisle.GetProperty("items").EnumerateArray())
            {
                total += item.GetProperty("grams").GetDouble();
            }
        }

        return total;
    }
}
