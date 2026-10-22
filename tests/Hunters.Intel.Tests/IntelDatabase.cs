using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NewEdenSoft.Hunters.Intel.Activity;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Sde;
using Testcontainers.PostgreSql;

namespace NewEdenSoft.Hunters.Intel.Tests;

/// <summary>One PostgreSQL container for the intel tests (requires Docker), with the <c>intel</c> schema migrated.</summary>
public sealed class IntelDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var services = Services(new FakeSystemActivity());
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IntelDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>The intel services against this database, with ESI and the SDE replaced by fakes.</summary>
    public ServiceProvider Services(FakeSystemActivity activity, Action<IntelOptions>? configure = null, FakeSde? sde = null)
    {
        sde ??= new FakeSde();
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        services.AddOptions<IntelOptions>().Configure(o => configure?.Invoke(o));
        services.AddDbContext<IntelDbContext>(o => o.UseNpgsql(ConnectionString, IntelDbContext.ConfigureNpgsql));
        services.AddIntelServices();
        services.RemoveAll<ISystemActivitySource>();
        services.AddSingleton<ISystemActivitySource>(activity);
        services.AddSingleton<ISdeMapSource>(sde);
        services.AddSingleton<ISdeTypeSource>(sde);
        return services.BuildServiceProvider();
    }
}

/// <summary>Intel database tests share one container and run one class at a time.</summary>
[CollectionDefinition(Name)]
public sealed class IntelDatabaseCollection : ICollectionFixture<IntelDatabase>
{
    public const string Name = "intel-database";
}
