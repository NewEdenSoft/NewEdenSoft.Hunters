using Microsoft.EntityFrameworkCore;
using NewEdenSoft.Hunters.Intel.Data;

namespace NewEdenSoft.Hunters.Server.Features.Activity;

public static class ActivityFeature
{
    /// <summary>
    /// Serves the intel service's activity tables, read from the same database (the <c>intel</c>
    /// schema, which the intel service migrates; never this server).
    /// </summary>
    public static IHostApplicationBuilder AddActivityFeature(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<IntelDbContext>(IntelDbContext.ConnectionName,
            configureDbContextOptions: o => o.UseNpgsql(IntelDbContext.ConfigureNpgsql));
        builder.Services.AddSingleton<ActivityStore>();
        builder.Services.AddSingleton<ActivityService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ActivityService>());
        return builder;
    }
}
