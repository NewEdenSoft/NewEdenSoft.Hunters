using Microsoft.EntityFrameworkCore;

namespace NewEdenSoft.Hunters.Server.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    /// <summary>PostgreSQL through EF Core; connection string <c>ConnectionStrings:hunters</c>.</summary>
    public static IHostApplicationBuilder AddPersistence(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<HuntersDbContext>(HuntersDbContext.ConnectionName);
        return builder;
    }

    /// <summary>Applies pending migrations.</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HuntersDbContext>().Database.MigrateAsync();
    }
}
