using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NewEdenSoft.Hunters.Server.Tests;

/// <summary>
/// Hosts the server in memory. No database is contacted unless a test resolves the DbContext.
/// </summary>
public sealed class HuntersServerFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["ConnectionStrings:hunters"] = "Host=localhost;Database=hunters_tests",
        ["Hunters:Web:Enabled"] = "false",
    };

    private readonly string _tempRoot = Directory.CreateTempSubdirectory("hunters-tests-").FullName;

    public HuntersServerFactory()
    {
        _settings["Hunters:DataProtection:KeysPath"] = Path.Combine(_tempRoot, "keys");
    }

    public HuntersServerFactory With(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);
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
