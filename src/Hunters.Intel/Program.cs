using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddIntel();

var host = builder.Build();

if (host.Services.GetRequiredService<IOptions<IntelOptions>>().Value.Database.MigrateOnStartup)
    await host.MigrateAsync();

await host.RunAsync();
