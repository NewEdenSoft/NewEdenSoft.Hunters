using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace NewEdenSoft.Hunters.Intel.Data;

/// <summary>
/// The intel data (docs/decisions.md, D22), in its own <c>intel</c> schema of the Hunters database.
/// The intel service writes it and owns its migrations; the API server only reads it.
/// </summary>
public sealed class IntelDbContext(DbContextOptions<IntelDbContext> options) : DbContext(options)
{
    public const string Schema = "intel";

    /// <summary>Connection string name, shared with the API server (<c>ConnectionStrings__hunters</c>).</summary>
    public const string ConnectionName = "hunters";

    public DbSet<SystemKillsSnapshot> SystemKills => Set<SystemKillsSnapshot>();

    public DbSet<SystemJumpsSnapshot> SystemJumps => Set<SystemJumpsSnapshot>();

    /// <summary>Keeps this context's migration history in its own schema, apart from the API server's.</summary>
    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(Schema);

        // One row per hourly snapshot, keyed by the end of its hour, so storing one twice is impossible.
        model.Entity<SystemKillsSnapshot>(e => e.HasKey(s => s.PeriodEnd));
        model.Entity<SystemJumpsSnapshot>(e => e.HasKey(s => s.PeriodEnd));
    }
}

/// <summary>Lets <c>dotnet ef</c> build the model without the intel service's host.</summary>
public sealed class IntelDbContextDesignFactory : IDesignTimeDbContextFactory<IntelDbContext>
{
    public IntelDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IntelDbContext>()
            .UseNpgsql("Host=localhost;Database=hunters", IntelDbContext.ConfigureNpgsql)
            .Options);
}
