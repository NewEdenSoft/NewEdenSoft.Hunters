using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;
using NewEdenSoft.Hunters.Sde;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests;

/// <summary>
/// Hosts the server in memory with EVE SSO and ESI replaced by fakes. Without
/// <see cref="WithDatabase"/> no database is contacted unless a test resolves the DbContext.
/// </summary>
public sealed class HuntersServerFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["ConnectionStrings:hunters"] = "Host=localhost;Database=hunters_tests",
        ["Hunters:Database:MigrateOnStartup"] = "false",
        ["Hunters:Web:Enabled"] = "false",
        ["Hunters:Sso:ClientId"] = "test-client",
        ["Hunters:Sso:ClientSecret"] = "test-secret",
        ["Hunters:Sso:CallbackUrl"] = "http://localhost/auth/eve/callback",
        // Activity tests load the intel tables themselves (ActivityService.RefreshAsync).
        ["Hunters:Activity:Enabled"] = "false",
    };

    private readonly string _tempRoot = Directory.CreateTempSubdirectory("hunters-tests-").FullName;

    public HuntersServerFactory()
    {
        _settings["Hunters:DataProtection:KeysPath"] = Path.Combine(_tempRoot, "keys");
    }

    public FakeEveSso Sso { get; } = new();

    public FakeAffiliations Affiliations { get; } = new();

    public FakeSdeMap Sde { get; } = new();

    public HuntersServerFactory With(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    /// <summary>Uses a real PostgreSQL database and applies migrations at startup.</summary>
    public HuntersServerFactory WithDatabase(string connectionString) =>
        With("ConnectionStrings:hunters", connectionString).With("Hunters:Database:MigrateOnStartup", "true");

    /// <summary>Enables web hosting with a web root containing the given files.</summary>
    public HuntersServerFactory WithWebApp(params (string Path, string Content)[] files)
    {
        var webRoot = Path.Combine(_tempRoot, "wwwroot");
        foreach (var (path, content) in files)
        {
            var fullPath = Path.Combine(webRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        _settings["Hunters:Web:Enabled"] = "true";
        _settings[WebHostDefaults.WebRootKey] = webRoot;
        return this;
    }

    /// <summary>A client that does not follow redirects, so login flows can be stepped through.</summary>
    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEveSsoClient>();
            services.AddSingleton<IEveSsoClient>(Sso);
            services.RemoveAll<IEveAffiliationService>();
            services.AddSingleton<IEveAffiliationService>(Affiliations);
            services.RemoveAll<ISdeMapSource>();
            services.AddSingleton<ISdeMapSource>(Sde);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch (IOException) { }
        }
    }
}
