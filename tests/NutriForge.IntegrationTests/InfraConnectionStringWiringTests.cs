using System.Text.RegularExpressions;

namespace NutriForge.IntegrationTests;

/// <summary>
/// Deployment-wiring guard (a static config check — boots nothing). The connection-string env-var
/// NAMES that Terraform injects into the Azure Container Apps must be EXACTLY the connection names the
/// app resolves at runtime.
///
/// The app reads its databases via <c>GetConnectionString("appdb"/"auditdb")</c> and its cache via
/// <c>AddRedisClient("cache")</c> (see <c>NutriForge.Infrastructure/DependencyInjection.cs</c>); those
/// are the Aspire resource names declared in <c>AppHost.cs</c>. .NET configuration maps a connection
/// name <c>X</c> to the env var <c>ConnectionStrings__X</c>. Locally Aspire injects the right names; in
/// Azure that is Terraform's job (<c>infra/main.tf</c>). A drift between the two resolves the connection
/// string to <c>null</c> and fails the app at startup — and it passes every local run because Aspire is
/// correct, so only a deploy surfaces it. This test exists to catch that drift in CI before a deploy.
/// </summary>
public sealed partial class InfraConnectionStringWiringTests
{
    [Fact]
    public void Terraform_injects_exactly_the_connection_string_names_the_app_reads()
    {
        var root = RepoRoot();
        var di = File.ReadAllText(Path.Combine(root, "src", "NutriForge.Infrastructure", "DependencyInjection.cs"));
        var tf = File.ReadAllText(Path.Combine(root, "infra", "main.tf"));

        // The single source of truth: the connection names the running app actually resolves.
        var appReads = AppConnectionNames().Matches(di).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(appReads); // guard against a silently-broken parse making the checks vacuous

        // The connection-string env vars Terraform injects (declared as: name = "ConnectionStrings__X").
        var tfInjects = InjectedConnectionNames().Matches(tf).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        // Every name the app reads must be injected — a missing one resolves to null and 500s at startup.
        var missing = appReads.Except(tfInjects).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0,
            $"infra/main.tf does not inject ConnectionStrings__ for: [{string.Join(", ", missing)}]. " +
            "The deployed app reads these via GetConnectionString/AddRedisClient; a missing one resolves to a " +
            "null connection string and fails the Container App at startup.");

        // ...and no STALE/extra one (e.g. the old ConnectionStrings__Default/__Audit/__Redis) — an env var
        // the app never reads is dead config that masks a half-finished rename.
        var stale = tfInjects.Except(appReads).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0,
            $"infra/main.tf injects ConnectionStrings__ env vars the app never reads: [{string.Join(", ", stale)}]. " +
            "Either the env-var name drifted from the code, or it is leftover from a rename.");
    }

    [Fact]
    public void Both_the_api_app_and_the_import_job_receive_the_redis_connection()
    {
        var tf = File.ReadAllText(Path.Combine(RepoRoot(), "infra", "main.tf"));

        // Postgres is shared via local.common_env, but the Redis connection is a per-container secret_name
        // binding declared once in the API app and once in the import job. A half-fix (updating only one
        // container) is a real failure mode, so pin that BOTH get ConnectionStrings__cache.
        var cacheDeclarations = InjectedConnectionNames().Matches(tf).Count(m => m.Groups[1].Value == "cache");
        Assert.True(cacheDeclarations >= 2,
            $"expected ConnectionStrings__cache wired into BOTH the API app and the import job; " +
            $"found {cacheDeclarations} declaration(s) in infra/main.tf.");
    }

    /// <summary>Captures the connection name from each GetConnectionString("X") / AddRedisClient("X") call.</summary>
    [GeneratedRegex("(?:GetConnectionString|AddRedisClient)\\(\\s*\"([^\"]+)\"")]
    private static partial Regex AppConnectionNames();

    /// <summary>Captures X from each Terraform env declaration: name = "ConnectionStrings__X".</summary>
    [GeneratedRegex("name\\s*=\\s*\"ConnectionStrings__([^\"]+)\"")]
    private static partial Regex InjectedConnectionNames();

    /// <summary>Walk up from the test assembly until the directory that contains infra/main.tf (the repo root).</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "infra", "main.tf")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repo root (no ancestor directory contains infra/main.tf).");
    }
}
