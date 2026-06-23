namespace NutriForge.IntegrationTests;

// Smoke test: boot the whole AppHost (real Postgres + Redis containers via Aspire)
// and assert the API's health endpoint responds. Requires a container runtime, so
// it runs locally / in CI with Docker, not on a bare build agent.
public class ApiHealthTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task Api_health_endpoint_returns_ok()
    {
        var ct = CancellationToken.None;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.NutriForge_AppHost>(ct);

        await using var app = await appHost.BuildAsync(ct).WaitAsync(DefaultTimeout, ct);
        await app.StartAsync(ct).WaitAsync(DefaultTimeout, ct);

        using var http = app.CreateHttpClient("api");
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("api", ct)
            .WaitAsync(DefaultTimeout, ct);

        using var response = await http.GetAsync("/health", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
