using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace NutriForge.IntegrationTests;

/// <summary>
/// Boots the whole NutriForge AppHost once per test collection — real Postgres (operational +
/// audit) and Redis containers, real connection strings injected by Aspire, real migrations
/// applied at startup. The SPA (npm app) is skipped via <c>SKIP_NPM_APPS</c> so tests don't
/// start the Vite dev server.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    public DistributedApplication App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("SKIP_NPM_APPS", "true");

        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.NutriForge_AppHost>();

        // The API serves over HTTPS with the local dev cert, which isn't trusted on CI agents.
        // Accept any server cert for the TEST clients only (this never ships in the app).
        builder.Services.ConfigureHttpClientDefaults(http =>
            http.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            }));

        App = await builder.BuildAsync();
        await App.StartAsync().WaitAsync(StartupTimeout);

        // StartAsync returns before resources are ready — wait for the API process to be running.
        await App.ResourceNotifications
            .WaitForResourceAsync("api", KnownResourceStates.Running)
            .WaitAsync(StartupTimeout);

        _ = CaptureApiLogsAsync();
    }

    private readonly ConcurrentQueue<string> _apiLogs = new();

    private async Task CaptureApiLogsAsync()
    {
        try
        {
            var loggerService = App.Services.GetRequiredService<ResourceLoggerService>();
            await foreach (var batch in loggerService.WatchAsync("api"))
            {
                foreach (var line in batch)
                {
                    _apiLogs.Enqueue(line.Content);
                }
            }
        }
#pragma warning disable CA1031
        catch
        {
            // best-effort log capture for diagnostics
        }
#pragma warning restore CA1031
    }

    /// <summary>Recent API log lines (optionally filtered) — for diagnosing background-worker behaviour.</summary>
    public string DumpApiLogs(string? contains = null)
    {
        var lines = _apiLogs.ToArray();
        if (contains is not null)
        {
            lines = lines.Where(l => l.Contains(contains, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        return string.Join('\n', lines.TakeLast(40));
    }

    public async Task DisposeAsync() => await App.DisposeAsync();

    /// <summary>
    /// An HTTP client for the API, pre-authenticated as a test user via the Development dev-auth
    /// scheme, and only returned once the API actually answers requests (migrations applied).
    /// </summary>
    public async Task<HttpClient> CreateReadyClientAsync(string subject = "itest-user", string role = "user")
    {
        var client = App.CreateHttpClient("api");
        client.Timeout = TimeSpan.FromSeconds(100);
        client.DefaultRequestHeaders.Add("X-Debug-Subject", subject);
        client.DefaultRequestHeaders.Add("X-Debug-Role", role);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Poll the anonymous root until the app is serving (startup migrations + seeding done).
        var deadline = DateTime.UtcNow + StartupTimeout;
        while (true)
        {
            try
            {
                using var res = await client.GetAsync("/");
                if (res.StatusCode == HttpStatusCode.OK)
                {
                    return client;
                }
            }
            catch (HttpRequestException)
            {
                // not up yet
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("API did not become ready within the startup timeout.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}

[CollectionDefinition("AppHost")]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture>;
