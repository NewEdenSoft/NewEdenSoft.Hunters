using Microsoft.EntityFrameworkCore;
using NewEdenSoft.Hunters.Intel.Activity;
using NewEdenSoft.Hunters.Intel.Data;
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
        services.AddSingleton<ISystemActivitySource, EsiSystemActivitySource>();
        services.AddSingleton<SystemActivityService>();
        services.AddHostedService(sp => sp.GetRequiredService<SystemActivityService>());
        return services;
    }

    /// <summary>Applies pending migrations of the <c>intel</c> schema.</summary>
    public static async Task MigrateAsync(this IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IntelDbContext>().Database.MigrateAsync();
    }
}
