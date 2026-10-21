using Microsoft.EntityFrameworkCore;

namespace NewEdenSoft.Hunters.Server.Infrastructure.Persistence;

public sealed class HuntersDbContext(DbContextOptions<HuntersDbContext> options) : DbContext(options)
{
    /// <summary>Connection string name, shared by Aspire and Docker Compose (<c>ConnectionStrings__hunters</c>).</summary>
    public const string ConnectionName = "hunters";

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<AccessKey> AccessKeys => Set<AccessKey>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>(e =>
        {
            e.HasMany(a => a.Characters).WithOne(c => c.Account).HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(a => a.Sessions).WithOne(s => s.Account).HasForeignKey(s => s.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Character>(e =>
        {
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(c => c.AccountId);
        });

        model.Entity<Session>(e =>
        {
            e.Property(s => s.Kind).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(s => s.TokenHash).IsUnique();
            e.HasIndex(s => s.AccountId);
        });

        model.Entity<AccessKey>(e => e.HasIndex(k => k.KeyHash).IsUnique());
    }
}
