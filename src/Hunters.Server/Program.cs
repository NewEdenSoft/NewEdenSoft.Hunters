using Microsoft.AspNetCore.DataProtection;
using NewEdenSoft.Hunters.Server.Configuration;
using NewEdenSoft.Hunters.Server.Features.Accounts;
using NewEdenSoft.Hunters.Server.Features.Admin;
using NewEdenSoft.Hunters.Server.Features.Admission;
using NewEdenSoft.Hunters.Server.Features.Auth;
using NewEdenSoft.Hunters.Server.Features.Discovery;
using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Infrastructure;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;
using NewEdenSoft.Hunters.Server.Infrastructure.Persistence;
using NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOptions<HuntersOptions>()
    .Bind(builder.Configuration.GetSection(HuntersOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var hunters = builder.Configuration.GetSection(HuntersOptions.SectionName).Get<HuntersOptions>() ?? new HuntersOptions();

// Infrastructure
builder.AddPersistence();
builder.Services.AddDataProtection()
    .SetApplicationName("NewEdenSoft.Hunters")
    .PersistKeysToFileSystem(new DirectoryInfo(hunters.DataProtection.KeysPath
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewEdenSoft", "Hunters", "keys")));
builder.Services.AddInMemoryInfrastructure();
builder.Services.AddEveServices(builder.Configuration);
builder.Services.AddOpenApi();

// Features
builder.Services.AddAuthFeature(hunters.Sso);
builder.Services.AddAccountsFeature();
builder.Services.AddAdmissionFeature();
builder.Services.AddMapFeature();

var app = builder.Build();

if (!hunters.Sso.IsConfigured)
{
    app.Logger.LogWarning(
        "EVE SSO is not configured (Hunters:Sso:ClientId, ClientSecret, CallbackUrl). Logins will fail until it is.");
}

if (hunters.Database.MigrateOnStartup)
    await app.MigrateDatabaseAsync();

if (hunters.Web.Enabled)
    app.UseWebApp();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapDefaultEndpoints();
app.MapDiscovery();
app.MapAuth();
app.MapAccounts();
app.MapAdmin();
app.MapMap();

if (hunters.Web.Enabled)
    app.MapWebAppFallback();

app.Run();
