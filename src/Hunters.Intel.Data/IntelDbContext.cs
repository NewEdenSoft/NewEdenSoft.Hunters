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

    public DbSet<SystemActivityWeek> SystemActivityWeek => Set<SystemActivityWeek>();

    public DbSet<RegionActivityWeek> RegionActivityWeek => Set<RegionActivityWeek>();

    public DbSet<ActivityWeekSamples> ActivityWeekSamples => Set<ActivityWeekSamples>();

    public DbSet<SystemActivityCurrent> SystemActivityCurrent => Set<SystemActivityCurrent>();

    public DbSet<RegionActivityCurrent> RegionActivityCurrent => Set<RegionActivityCurrent>();

    public DbSet<IntelVersion> Versions => Set<IntelVersion>();

    public DbSet<Kill> Kills => Set<Kill>();

    public DbSet<FeedPosition> FeedPositions => Set<FeedPosition>();

    /// <summary>Keeps this context's migration history in its own schema, apart from the API server's.</summary>
    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(Schema);

        // One row per hourly snapshot, keyed by the end of its hour, so storing one twice is impossible.
        model.Entity<SystemKillsSnapshot>(e => e.HasKey(s => s.PeriodEnd));
        model.Entity<SystemJumpsSnapshot>(e => e.HasKey(s => s.PeriodEnd));

        // Derived tables (docs/decisions.md, D22), updated as raw hours are applied and expire.
        model.Entity<SystemActivityWeek>(e => e.HasKey(w => new { w.SystemId, w.HourOfWeek }));
        model.Entity<RegionActivityWeek>(e => e.HasKey(w => new { w.RegionId, w.HourOfWeek }));
        model.Entity<ActivityWeekSamples>(e => e.HasKey(s => s.HourOfWeek));
        model.Entity<SystemActivityCurrent>(e =>
        {
            e.HasKey(c => c.SystemId);
            e.Property(c => c.SystemId).ValueGeneratedNever();
            OwnMetrics(e);
        });
        model.Entity<RegionActivityCurrent>(e =>
        {
            e.HasKey(c => c.RegionId);
            e.Property(c => c.RegionId).ValueGeneratedNever();
            OwnMetrics(e);
        });
        model.Entity<IntelVersion>(e =>
        {
            e.HasKey(v => v.Dataset);
            e.Property(v => v.Dataset).HasMaxLength(32);
        });

        // zKillboard kills: by time for the retention and recent windows, by system and time for fleet battles.
        model.Entity<Kill>(e =>
        {
            e.HasKey(k => k.KillmailId);
            e.Property(k => k.KillmailId).ValueGeneratedNever();
            e.Property(k => k.Hash).HasMaxLength(64);
            e.HasIndex(k => k.Time);
            e.HasIndex(k => new { k.SystemId, k.Time });
        });
        model.Entity<FeedPosition>(e =>
        {
            e.HasKey(f => f.Feed);
            e.Property(f => f.Feed).HasMaxLength(32);
        });
    }

    /// <summary>Each metric as a set of columns (<c>NpcKills_LastHour</c>, …) in the current row.</summary>
    private static void OwnMetrics<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e) where T : class
    {
        foreach (var metric in ActivityMetrics.All)
            e.OwnsOne(typeof(ActivityMetric), metric);
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
