using Microsoft.AspNetCore.DataProtection;
using NewEdenSoft.Hunters.Server.Configuration;
using NewEdenSoft.Hunters.Server.Data;
using NewEdenSoft.Hunters.Server.Discovery;
using NewEdenSoft.Hunters.Server.Infrastructure;
using NewEdenSoft.Hunters.Server.WebHosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOptions<HuntersOptions>()
    .Bind(builder.Configuration.GetSection(HuntersOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var hunters = builder.Configuration.GetSection(HuntersOptions.SectionName).Get<HuntersOptions>() ?? new HuntersOptions();

builder.AddNpgsqlDbContext<HuntersDbContext>(HuntersDbContext.ConnectionName);

var keysPath = hunters.DataProtection.KeysPath
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewEdenSoft", "Hunters", "keys");
builder.Services.AddDataProtection()
    .SetApplicationName("NewEdenSoft.Hunters")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddInMemoryInfrastructure();
builder.Services.AddOpenApi();

var app = builder.Build();

if (hunters.Web.Enabled)
    app.UseWebApp();

app.MapOpenApi();
app.MapDefaultEndpoints();
app.MapDiscovery();

if (hunters.Web.Enabled)
    app.MapWebAppFallback();

app.Run();
