using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddIntel();

var host = builder.Build();

if (host.Services.GetRequiredService<IOptions<IntelOptions>>().Value.Database.MigrateOnStartup)
    await host.MigrateAsync();

// One-off commands run and exit without starting the collectors.
if (args.Contains("rebuild-activity"))
{
    await host.RebuildActivityAsync(CancellationToken.None);
    return;
}

await host.RunAsync();
