using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NutriForge.Api.Setup;

namespace NutriForge.IntegrationTests;

/// <summary>
/// The auth fail-closed guard (#56): the header-based dev scheme (which trusts X-Debug-Subject/Role)
/// is allowed ONLY in Development. A deployed environment with no OIDC Authority configured must refuse
/// to start rather than silently expose an identity/role bypass.
/// </summary>
public sealed class AuthSetupTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void Refuses_to_start_outside_development_without_an_authority()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddNutriForgeAuth(Config(), new FakeEnv("Production")));
        Assert.Contains("Authority", ex.Message);
    }

    [Fact]
    public void Allows_the_dev_scheme_in_development_without_an_authority()
    {
        var services = new ServiceCollection();
        // Does not throw — the dev header scheme is fine locally.
        services.AddNutriForgeAuth(Config(), new FakeEnv("Development"));
    }

    [Fact]
    public void Allows_a_deployed_environment_when_an_oidc_authority_is_configured()
    {
        var services = new ServiceCollection();
        services.AddNutriForgeAuth(
            Config(("Authentication:Authority", "https://login.example.com/")), new FakeEnv("Production"));
    }

    private sealed class FakeEnv(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
