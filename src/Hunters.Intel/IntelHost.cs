using Microsoft.EntityFrameworkCore;
using NewEdenSoft.Hunters.Intel.Activity;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Hunting;
using NewEdenSoft.Hunters.Intel.Kills;
using NewEdenSoft.Hunters.Intel.Sde;
using NewEdenSoft.Hunters.Sde;

namespace NewEdenSoft.Hunters.Intel;

/// <summary>
/// The intel service (docs/decisions.md, D22): a worker next to the API server that collects public
/// activity data and writes the <c>intel</c> schema. No HTTP endpoints; the API server serves the results.
/// </summary>
public static class IntelHost
{
    public static IHostApplicationBuilder AddIntel(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<IntelOptions>().Bind(builder.Configuration.GetSection(IntelOptions.SectionName));
        builder.AddNpgsqlDbContext<IntelDbContext>(IntelDbContext.ConnectionName,
            configureDbContextOptions: options => options.UseNpgsql(IntelDbContext.ConfigureNpgsql));

        var contact = builder.Configuration[$"{IntelOptions.SectionName}:Esi:Contact"] ?? new EsiOptions().Contact;
        builder.Services.AddEveEsi(o =>
        {
            o.ApplicationName = "NewEdenSoft.Hunters.Intel";
            o.Contact = contact;
        });
        builder.Services.AddSde(o =>
        {
            o.CachePath = builder.Configuration[$"{IntelOptions.SectionName}:Sde:CachePath"];
            o.Contact = contact;
        });
        builder.Services.AddIntelServices();
        return builder;
    }

    /// <summary>The collectors, without the database, ESI and SDE registrations (tests bring their own).</summary>
    public static IServiceCollection AddIntelServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SdeCatalogStore>();
        services.AddSingleton<SdeCatalogService>();
        services.AddHostedService(sp => sp.GetRequiredService<SdeCatalogService>());
        services.AddSingleton<ActivityAggregator>();
        services.AddSingleton<ActivityAggregatorService>();
        services.AddHostedService(sp => sp.GetRequiredService<ActivityAggregatorService>());
        services.AddSingleton<ISystemActivitySource, EsiSystemActivitySource>();
        services.AddSingleton<SystemActivityService>();
        services.AddHostedService(sp => sp.GetRequiredService<SystemActivityService>());
        services.AddSingleton<HuntingProfileStore>();
        services.AddSingleton<HuntingProfileService>();
        services.AddHostedService(sp => sp.GetRequiredService<HuntingProfileService>());
        services.AddHttpClient<IKillFeedSource, R2Z2KillFeedSource>(R2Z2KillFeedSource.Configure)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
        services.AddSingleton<KillFeedService>();
        services.AddHostedService(sp => sp.GetRequiredService<KillFeedService>());
        return services;
    }

    /// <summary>
    /// <c>rebuild-activity</c>: recomputes the activity tables from the raw hours, for when the rules
    /// change. Safe while the service runs; the two take turns on a database lock.
    /// </summary>
    public static async Task RebuildActivityAsync(this IHost host, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        await host.Services.GetRequiredService<SdeCatalogService>().LoadAsync(cancellationToken);
        if (host.Services.GetRequiredService<SdeCatalogStore>().Current is null)
            throw new InvalidOperationException("The SDE could not be loaded; see the log above.");
        await host.Services.GetRequiredService<ActivityAggregator>().RebuildAsync(cancellationToken);
        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IntelHost))
            .LogInformation("Activity rebuilt in {Elapsed}", TimeProvider.System.GetElapsedTime(started));
    }

    /// <summary>Applies pending migrations of the <c>intel</c> schema.</summary>
    public static async Task MigrateAsync(this IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IntelDbContext>().Database.MigrateAsync();
    }
}
