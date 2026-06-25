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
    private static readonly string[] HighProteinDinnerTags = ["high-protein", "dinner"];

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

    /// <summary>Notification opt-in + the secure account-link flow (#95): subscribe, mint a code, redeem it once.</summary>
    [Fact]
    public async Task Channel_subscription_and_secure_account_linking_round_trip()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"sub-{Guid.NewGuid():N}");

        // Opt in + set the daily send hour.
        using (var put = await client.PutAsJsonAsync("/api/v1/notifications/subscription",
            new { channel = "telegram", enabled = true, sendHourUtc = 9 }, Json))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var dto = await put.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.True(dto.GetProperty("enabled").GetBoolean());
            Assert.Equal(9, dto.GetProperty("sendHourUtc").GetInt32());
            Assert.False(dto.GetProperty("isLinked").GetBoolean());
        }

        // Mint a one-time link code.
        using var linkRes = await client.PostAsJsonAsync("/api/v1/notifications/link", new { channel = "telegram" }, Json);
        Assert.Equal(HttpStatusCode.OK, linkRes.StatusCode);
        var code = (await linkRes.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("code").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(code));

        // Redeem it → the subscription is now linked.
        using (var redeem = await client.PostAsJsonAsync("/api/v1/notifications/link/redeem",
            new { code, address = "chat-42" }, Json))
        {
            Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);
            Assert.True((await redeem.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("linked").GetBoolean());
        }

        var sub = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/subscription?channel=telegram", Json);
        Assert.True(sub.GetProperty("isLinked").GetBoolean());
        Assert.Equal("chat-42", sub.GetProperty("address").GetString());

        // Single-use: redeeming the same code again is rejected (422).
        using (var again = await client.PostAsJsonAsync("/api/v1/notifications/link/redeem",
            new { code, address = "chat-99" }, Json))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        }

        // A bogus code is rejected too.
        using var bad = await client.PostAsJsonAsync("/api/v1/notifications/link/redeem",
            new { code = "not-a-real-code", address = "chat-1" }, Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
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

    /// <summary>A YouTube import still needs the AI extractor (the description is messy text) — 503 without it.</summary>
    [Fact]
    public async Task Recipe_import_preview_degrades_to_503_without_a_provider()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"import-{Guid.NewGuid():N}");
        using var res = await client.PostAsJsonAsync("/api/v1/recipes/import/preview",
            new { url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ" }, Json);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    /// <summary>
    /// Web recipe import via schema.org JSON-LD (#91) is deterministic — pasted recipe HTML produces a
    /// draft with computed nutrition WITHOUT any AI provider configured.
    /// </summary>
    [Fact]
    public async Task Recipe_import_from_pasted_json_ld_html_works_without_an_ai_provider()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"jsonld-{Guid.NewGuid():N}");
        const string html =
            "<html><head><script type=\"application/ld+json\">" +
            "{\"@context\":\"https://schema.org\",\"@type\":\"Recipe\",\"name\":\"JSON-LD Bowl\"," +
            "\"recipeYield\":\"2\",\"totalTime\":\"PT15M\"," +
            "\"recipeIngredient\":[\"200 g chicken\",\"150 g white rice\"]," +
            "\"recipeInstructions\":\"Cook it.\"}</script></head><body></body></html>";

        using var res = await client.PostAsJsonAsync("/api/v1/recipes/import/preview", new { text = html }, Json);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode); // deterministic path — no AI needed
        var preview = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        var draft = preview.GetProperty("draft");
        Assert.Equal("JSON-LD Bowl", draft.GetProperty("name").GetString());
        Assert.Equal(2, draft.GetProperty("servings").GetInt32());
        Assert.True(draft.GetProperty("ingredients").GetArrayLength() >= 2);
        // chicken + white rice resolve against the seeded catalog ⇒ computed per-serving nutrition.
        Assert.True(draft.GetProperty("kcalPerServing").GetDouble() > 0, "expected computed nutrition from resolved ingredients");
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

    /// <summary>Members scale the shopping list by the SUM of their portion factors (owner is auto-added).</summary>
    [Fact]
    public async Task Plan_members_scale_the_shopping_list_by_the_sum_of_portion_factors()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"members-shop-{Guid.NewGuid():N}");
        var solo = await CreateReadyPlanAsync(client, eaters: 1);
        // owner 2000 (factor 1.0) + a 1000-kcal member (factor 0.5) ⇒ multiplier 1.5.
        var couple = await CreateReadyPlanAsync(client, eaters: null,
            members: new object[] { new { name = "GF", targetKcal = 1000.0 } });

        Assert.Equal(1.5, couple.GetProperty("portionMultiplier").GetDouble(), 3);

        var gramsSolo = await ShoppingGramsTotalAsync(client, solo.GetProperty("id").GetString()!);
        var gramsCouple = await ShoppingGramsTotalAsync(client, couple.GetProperty("id").GetString()!);
        Assert.True(gramsSolo > 0);
        Assert.InRange(gramsCouple / gramsSolo, 1.48, 1.52); // weighted-sum groceries
    }

    /// <summary>Members NEVER change the generated per-primary plan (generation isolation, generalized).</summary>
    [Fact]
    public async Task Plan_members_do_not_change_the_generated_per_primary_plan()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"members-gen-{Guid.NewGuid():N}");
        var solo = await CreateReadyPlanAsync(client, eaters: 1);
        var withMembers = await CreateReadyPlanAsync(client, eaters: null,
            members: new object[] { new { name = "GF", targetKcal = 1500.0 } });

        // The single-primary plan is byte-identical whether or not members are attached.
        Assert.Equal(solo.GetProperty("targetKcal").GetDouble(), withMembers.GetProperty("targetKcal").GetDouble());
        Assert.Equal(solo.GetProperty("achievedKcal").GetDouble(), withMembers.GetProperty("achievedKcal").GetDouble());
        var s1 = solo.GetProperty("slots");
        var s2 = withMembers.GetProperty("slots");
        Assert.Equal(s1.GetArrayLength(), s2.GetArrayLength());
        for (var i = 0; i < s1.GetArrayLength(); i++)
        {
            Assert.Equal(s1[i].GetProperty("servings").GetDouble(), s2[i].GetProperty("servings").GetDouble());
        }

        // The owner is auto-added (factor 1.0); the member's factor = 1500/2000 = 0.75.
        var members = withMembers.GetProperty("members");
        Assert.Equal(2, members.GetArrayLength());
        Assert.Equal("You", members[0].GetProperty("name").GetString());
        Assert.Equal(1.0, members[0].GetProperty("portionFactor").GetDouble(), 3);
        Assert.Equal(0.75, members[1].GetProperty("portionFactor").GetDouble(), 3);
    }

    // -------------------- Recipe ownership (global vs per-user) --------------------

    /// <summary>A user's recipe is private: visible to them, invisible (404) to everyone else.</summary>
    [Fact]
    public async Task A_users_recipe_is_private_and_invisible_to_other_users()
    {
        var alice = await fixture.CreateReadyClientAsync(subject: $"own-alice-{Guid.NewGuid():N}");
        var bob = await fixture.CreateReadyClientAsync(subject: $"own-bob-{Guid.NewGuid():N}");

        var recipe = await CreateRecipeAsync(alice, "Alice secret bowl");
        var id = recipe.GetProperty("id").GetString()!;
        Assert.False(recipe.GetProperty("isGlobal").GetBoolean());
        Assert.True(recipe.GetProperty("isMine").GetBoolean());

        // Alice sees it; Bob can't GET it and it's absent from his list.
        using (var mine = await alice.GetAsync($"/api/v1/recipes/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        }

        using (var theirs = await bob.GetAsync($"/api/v1/recipes/{id}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        }

        var bobList = await bob.GetFromJsonAsync<JsonElement>("/api/v1/recipes", Json);
        Assert.DoesNotContain(bobList.EnumerateArray(), r => r.GetProperty("id").GetString() == id);
    }

    /// <summary>Seeded recipes are global — every user sees them, flagged isGlobal.</summary>
    [Fact]
    public async Task Seeded_recipes_are_global_and_visible_to_everyone()
    {
        var alice = await fixture.CreateReadyClientAsync(subject: $"glob-alice-{Guid.NewGuid():N}");
        var bob = await fixture.CreateReadyClientAsync(subject: $"glob-bob-{Guid.NewGuid():N}");

        var aliceList = await alice.GetFromJsonAsync<JsonElement>("/api/v1/recipes", Json);
        var bobList = await bob.GetFromJsonAsync<JsonElement>("/api/v1/recipes", Json);

        Assert.True(aliceList.GetArrayLength() > 0, "the dev seeder provides global recipes");
        Assert.All(aliceList.EnumerateArray(), r => Assert.True(r.GetProperty("isGlobal").GetBoolean()));
        // Two fresh users (no private recipes) see the SAME global catalog, by id.
        var aliceIds = aliceList.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToHashSet();
        var bobIds = bobList.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToHashSet();
        Assert.Equal(aliceIds, bobIds);
    }

    /// <summary>The Global intent flag is ignored for a non-admin: their recipe is still private.</summary>
    [Fact]
    public async Task Non_admin_cannot_create_a_global_recipe()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"noglob-{Guid.NewGuid():N}", role: "user");
        var recipe = await CreateRecipeAsync(client, "Sneaky global attempt", global: true);

        Assert.False(recipe.GetProperty("isGlobal").GetBoolean());
        Assert.True(recipe.GetProperty("isMine").GetBoolean());
    }

    /// <summary>An admin creates a real global recipe that other users can see.</summary>
    [Fact]
    public async Task Admin_can_create_a_global_recipe_visible_to_others()
    {
        var admin = await fixture.CreateReadyClientAsync(subject: $"adm-{Guid.NewGuid():N}", role: "admin");
        var other = await fixture.CreateReadyClientAsync(subject: $"adm-other-{Guid.NewGuid():N}");

        var recipe = await CreateRecipeAsync(admin, "Admin global bowl", global: true);
        var id = recipe.GetProperty("id").GetString()!;
        Assert.True(recipe.GetProperty("isGlobal").GetBoolean());

        using var seen = await other.GetAsync($"/api/v1/recipes/{id}");
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
        var dto = await seen.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(dto.GetProperty("isGlobal").GetBoolean());
        Assert.False(dto.GetProperty("isMine").GetBoolean()); // a global recipe is nobody's "mine"
    }

    /// <summary>A non-admin may not edit or delete a global (admin-curated) recipe — 403, not silent success.</summary>
    [Fact]
    public async Task Non_admin_cannot_edit_or_delete_a_global_recipe()
    {
        var admin = await fixture.CreateReadyClientAsync(subject: $"gedit-adm-{Guid.NewGuid():N}", role: "admin");
        var user = await fixture.CreateReadyClientAsync(subject: $"gedit-usr-{Guid.NewGuid():N}", role: "user");

        var global = await CreateRecipeAsync(admin, "Curated global", global: true);
        var id = global.GetProperty("id").GetString()!;

        using var put = await user.PutAsJsonAsync($"/api/v1/recipes/{id}",
            RecipeBody("Hijacked name"), Json);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);

        using var del = await user.DeleteAsync($"/api/v1/recipes/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
    }

    /// <summary>Another user's private recipe is a 404 on every write path (existence stays hidden).</summary>
    [Fact]
    public async Task A_foreign_private_recipe_is_404_on_edit_and_delete()
    {
        var alice = await fixture.CreateReadyClientAsync(subject: $"for-alice-{Guid.NewGuid():N}");
        var bob = await fixture.CreateReadyClientAsync(subject: $"for-bob-{Guid.NewGuid():N}");

        var id = (await CreateRecipeAsync(alice, "Alice private")).GetProperty("id").GetString()!;

        using var put = await bob.PutAsJsonAsync($"/api/v1/recipes/{id}", RecipeBody("nope"), Json);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        using var del = await bob.DeleteAsync($"/api/v1/recipes/{id}");
        Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);
    }

    /// <summary>The owner can edit and delete their own recipe.</summary>
    [Fact]
    public async Task Owner_can_edit_and_delete_their_recipe()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"crud-{Guid.NewGuid():N}");
        var id = (await CreateRecipeAsync(client, "Editable")).GetProperty("id").GetString()!;

        using (var put = await client.PutAsJsonAsync($"/api/v1/recipes/{id}", RecipeBody("Edited name"), Json))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var dto = await put.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.Equal("Edited name", dto.GetProperty("name").GetString());
        }

        using (var del = await client.DeleteAsync($"/api/v1/recipes/{id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        }

        using var gone = await client.GetAsync($"/api/v1/recipes/{id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    /// <summary>An admin promotes a private recipe to global; a non-admin is refused (403).</summary>
    [Fact]
    public async Task Admin_can_promote_a_recipe_to_global_and_non_admin_cannot()
    {
        var admin = await fixture.CreateReadyClientAsync(subject: $"promo-adm-{Guid.NewGuid():N}", role: "admin");
        var user = await fixture.CreateReadyClientAsync(subject: $"promo-usr-{Guid.NewGuid():N}", role: "user");

        // A non-admin cannot reach the promote surface at all (admin-gated).
        var userRecipe = (await CreateRecipeAsync(user, "User recipe")).GetProperty("id").GetString()!;
        using (var forbidden = await Post(user, $"/api/v1/recipes/{userRecipe}/promote-global"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        // The admin promotes their own private recipe → it becomes global and visible to the user.
        var adminRecipe = (await CreateRecipeAsync(admin, "Promote me")).GetProperty("id").GetString()!;
        using (var promote = await Post(admin, $"/api/v1/recipes/{adminRecipe}/promote-global"))
        {
            Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
            var dto = await promote.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.True(dto.GetProperty("isGlobal").GetBoolean());
        }

        using var seen = await user.GetAsync($"/api/v1/recipes/{adminRecipe}");
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
    }

    /// <summary>
    /// Security invariant for generation: every recipe the generator placed into a user's plan must be
    /// one that user can actually see (global ∪ own). A leak of another user's private recipe into the
    /// pool would surface here as a slot the owner can't GET (404).
    /// </summary>
    [Fact]
    public async Task Every_recipe_in_a_generated_plan_is_visible_to_the_plan_owner()
    {
        // Another user with a private, fully-resolved recipe that must never leak into anyone else's pool.
        var stranger = await fixture.CreateReadyClientAsync(subject: $"leak-stranger-{Guid.NewGuid():N}");
        await CreateRecipeAsync(stranger, "Stranger private high-protein", tags: HighProteinDinnerTags);

        var owner = await fixture.CreateReadyClientAsync(subject: $"leak-owner-{Guid.NewGuid():N}");
        var plan = await CreateReadyPlanAsync(owner, eaters: 1);

        var recipeIds = plan.GetProperty("slots").EnumerateArray()
            .Select(s => s.GetProperty("recipeId").GetString()!)
            .Distinct();

        foreach (var id in recipeIds)
        {
            using var res = await owner.GetAsync($"/api/v1/recipes/{id}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode); // 404 here would mean a foreign recipe leaked in
        }
    }

    /// <summary>
    /// Raw vs cooked yields (#85): a seeded recipe with cooked-basis rice (yield 2.8) and raw-basis
    /// chicken (yield 0.75) reports raw purchase weight below the cooked rice volume, and cooked chicken
    /// below its raw weight — the "cantidad cruda / cocida" columns.
    /// </summary>
    [Fact]
    public async Task Recipe_detail_reports_raw_and_cooked_weights_from_yield_factors()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"yield-{Guid.NewGuid():N}");

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/recipes", Json);
        var riceRecipe = list.EnumerateArray()
            .First(r => r.GetProperty("name").GetString()!.Contains("rice", StringComparison.OrdinalIgnoreCase));
        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/recipes/{riceRecipe.GetProperty("id").GetString()}", Json);
        var ingredients = detail.GetProperty("ingredients").EnumerateArray().ToList();

        // Rice is stated cooked ⇒ you buy LESS raw than the cooked weight (raw = cooked / 2.8).
        var rice = ingredients.First(i => i.GetProperty("name").GetString()!.Contains("rice", StringComparison.OrdinalIgnoreCase));
        var riceRaw = rice.GetProperty("rawGrams").GetDouble();
        var riceCooked = rice.GetProperty("cookedGrams").GetDouble();
        Assert.True(riceRaw < riceCooked, $"rice raw {riceRaw} should be < cooked {riceCooked}");
        Assert.Equal(riceCooked / 2.8, riceRaw, 1);

        // Chicken is stated raw ⇒ it loses water, so cooked < raw (cooked = raw × 0.75).
        var chicken = ingredients.FirstOrDefault(i => i.GetProperty("name").GetString()!.Contains("chicken", StringComparison.OrdinalIgnoreCase));
        if (chicken.ValueKind == JsonValueKind.Object)
        {
            var chRaw = chicken.GetProperty("rawGrams").GetDouble();
            var chCooked = chicken.GetProperty("cookedGrams").GetDouble();
            Assert.True(chCooked < chRaw, $"chicken cooked {chCooked} should be < raw {chRaw}");
            Assert.Equal(chRaw * 0.75, chCooked, 1);
        }
    }

    // ---- recipe-ownership helpers ----

    private static async Task<JsonElement> CreateRecipeAsync(
        HttpClient client, string name, bool global = false, string[]? tags = null)
    {
        using var res = await client.PostAsJsonAsync("/api/v1/recipes", RecipeBody(name, global, tags), Json);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static object RecipeBody(string name, bool global = false, string[]? tags = null) => new
    {
        name,
        servings = 2,
        totalMinutes = 10,
        instructions = "mix",
        tags = tags ?? new[] { "test" },
        ingredients = new[] { new { quantity = 100.0, unit = "g", name = "egg" } },
        global,
    };

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(req);
    }

    // -------------------- Day-block rotation (cook-once-eat-N-days) --------------------

    /// <summary>Block rotation round-trips and cooks ⌈horizon/blockSize⌉ distinct meal-sets, not one per day.</summary>
    [Fact]
    public async Task Day_blocks_cook_one_meal_set_per_block()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"blocks-{Guid.NewGuid():N}");

        var daily = await GenReadyBlockAsync(client, days: 6, blockSize: 1, eaters: 1);
        var blocked = await GenReadyBlockAsync(client, days: 6, blockSize: 3, eaters: 1);

        // A fresh meal-set every day ⇒ 6 distinct day-indices.
        Assert.Equal(6, daily.GetProperty("numBlocks").GetInt32());
        Assert.Equal(6, DistinctSlotDays(daily));

        // Two 3-day blocks ⇒ 2 distinct meal-sets, reported as such.
        Assert.Equal(6, blocked.GetProperty("horizonDays").GetInt32());
        Assert.Equal(3, blocked.GetProperty("blockSize").GetInt32());
        Assert.Equal(2, blocked.GetProperty("numBlocks").GetInt32());
        Assert.Equal(2, DistinctSlotDays(blocked));

        // The per-day average is unaffected by how the days are blocked (both ~ the 2000 target).
        Assert.InRange(daily.GetProperty("achievedKcal").GetDouble(), 1850, 2150);
        Assert.InRange(blocked.GetProperty("achievedKcal").GetDouble(), 1850, 2150);
    }

    /// <summary>
    /// A single block covering N days buys exactly N× the groceries of one covering 1 day (same recipes,
    /// scaled by the block's day-count), and eaters multiplies on top — block-days × people, no double count.
    /// </summary>
    [Fact]
    public async Task A_block_buys_its_day_count_times_the_groceries_and_eaters_compose()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"blocks-shop-{Guid.NewGuid():N}");

        // blockSize == horizon ⇒ ONE block. With one block the greedy selection is identical regardless of
        // horizon (same first meals, same servings), so the only difference is the block's day-count.
        var twoDays = await GenReadyBlockAsync(client, days: 2, blockSize: 2, eaters: 1);
        var fourDays = await GenReadyBlockAsync(client, days: 4, blockSize: 4, eaters: 1);
        var twoDaysTwoPeople = await GenReadyBlockAsync(client, days: 2, blockSize: 2, eaters: 2);

        var g2 = await ShoppingGramsTotalAsync(client, twoDays.GetProperty("id").GetString()!);
        var g4 = await ShoppingGramsTotalAsync(client, fourDays.GetProperty("id").GetString()!);
        var g2x2 = await ShoppingGramsTotalAsync(client, twoDaysTwoPeople.GetProperty("id").GetString()!);

        Assert.True(g2 > 0, "expected a non-empty shopping list");
        Assert.InRange(g4 / g2, 1.98, 2.02);   // 4-day block buys 2× a 2-day block (same meal-set)
        Assert.InRange(g2x2 / g2, 1.98, 2.02);  // 2 people buy 2× — composes with, doesn't double, block-days
    }

    /// <summary>
    /// The batch-cook guide (#86) emits one cook session per block with raw quantities grouped by
    /// appliance, portioned into (days × people), and its raw totals reconcile exactly with the shopping
    /// list — both are the same raw purchase weight, just organised cook-first vs buy-first.
    /// </summary>
    [Fact]
    public async Task Batch_cook_guide_groups_by_block_and_reconciles_with_the_shopping_list()
    {
        var client = await fixture.CreateReadyClientAsync(subject: $"batch-{Guid.NewGuid():N}");
        var plan = await GenReadyBlockAsync(client, days: 6, blockSize: 3, eaters: 2);
        var planId = plan.GetProperty("id").GetString()!;

        var guide = await client.GetFromJsonAsync<JsonElement>($"/api/v1/diet-plans/{planId}/batch-cook", Json);
        var blocks = guide.GetProperty("blocks");
        Assert.Equal(2, blocks.GetArrayLength()); // two 3-day blocks

        double batchRaw = 0;
        var methods = new HashSet<string>();
        foreach (var block in blocks.EnumerateArray())
        {
            Assert.Equal(6, block.GetProperty("portions").GetInt32()); // 3 days × 2 people
            Assert.True(block.GetProperty("steps").GetArrayLength() > 0, "each block cooks something");
            foreach (var step in block.GetProperty("steps").EnumerateArray())
            {
                methods.Add(step.GetProperty("method").GetString()!);
                Assert.Equal(6, step.GetProperty("portionInto").GetInt32());
                foreach (var ing in step.GetProperty("ingredients").EnumerateArray())
                {
                    batchRaw += ing.GetProperty("rawGrams").GetDouble();
                }
            }
        }

        Assert.True(batchRaw > 0);
        Assert.Contains(methods, m => !string.IsNullOrWhiteSpace(m)); // appliance grouping present

        // Cook-first totals == buy-first totals (raw weight; the fresh subject has an empty pantry).
        var shopRaw = await ShoppingGramsTotalAsync(client, planId);
        Assert.InRange(batchRaw / shopRaw, 0.98, 1.02);
    }

    private static int DistinctSlotDays(JsonElement plan) =>
        plan.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("day").GetInt32()).Distinct().Count();

    /// <summary>Create a Ready plan with an explicit day-block size + headcount.</summary>
    private static async Task<JsonElement> GenReadyBlockAsync(HttpClient client, int days, int blockSize, int eaters)
    {
        var body = new { dietSlug = "high-protein", horizonDays = days, kcalTarget = 2000.0, blockSize, eaters };
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/diet-plans")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
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

    /// <summary>Create a diet plan (fresh idempotency key) and poll until it is Ready.</summary>
    private static async Task<JsonElement> CreateReadyPlanAsync(HttpClient client, int? eaters, int days = 3, object? members = null)
    {
        object body = members is not null
            ? new { dietSlug = "high-protein", horizonDays = days, kcalTarget = 2000.0, members }
            : eaters is null
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
