using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Server.Features.Auth;
using NewEdenSoft.Hunters.Server.Configuration;
using NewEdenSoft.Hunters.Server.Infrastructure.Persistence;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Features.Admission;

/// <summary>
/// Periodically re-applies the admission rules to existing accounts, because characters change
/// corporation. Accounts that no longer qualify have their sessions revoked. If ESI is unavailable
/// the run is skipped rather than locking everyone out.
/// </summary>
public sealed partial class AdmissionRecheckService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<HuntersOptions> options,
    TimeProvider time,
    ILogger<AdmissionRecheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.CurrentValue.Admission.RecheckInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RecheckAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRecheckFailed(ex);
            }
        }
    }

    public async Task<int> RecheckAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var admission = scope.ServiceProvider.GetRequiredService<AdmissionService>();
        if (!admission.HasCombinableRules)
            return 0;

        var db = scope.ServiceProvider.GetRequiredService<HuntersDbContext>();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        var affiliations = scope.ServiceProvider.GetRequiredService<IEveAffiliationService>();
        var allCharacters = options.CurrentValue.Admission.Affiliation.Scope == AffiliationScope.AllCharacters;

        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => !a.IsDisabled && a.Sessions.Any(s => s.RevokedAt == null))
            .Select(a => new { a.Id, a.MainCharacterId, CharacterIds = a.Characters.Select(c => c.Id).ToList() })
            .ToListAsync(cancellationToken);

        var subjects = accounts.ToDictionary(
            a => a.Id,
            a => allCharacters ? a.CharacterIds : a.MainCharacterId is { } main ? [main] : a.CharacterIds.Take(1).ToList());

        IReadOnlyDictionary<long, CharacterAffiliation>? lookup = null;
        if (options.CurrentValue.Admission.Affiliation.Enabled)
        {
            try
            {
                lookup = await affiliations.GetAsync(subjects.Values.SelectMany(ids => ids).ToList(), cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                LogAffiliationUnavailable(ex);
                return 0;
            }
        }

        var revoked = 0;
        foreach (var (accountId, ids) in subjects)
        {
            if (ids.Count > 0 && ids.All(id => admission.Evaluate(id, lookup).Allowed))
                continue;

            revoked += await sessions.RevokeAllAsync(accountId, cancellationToken);
            LogAccountRevoked(accountId);
        }

        return revoked;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {AccountId} no longer meets the admission rules; sessions revoked")]
    private partial void LogAccountRevoked(Guid accountId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admission recheck skipped: affiliations could not be looked up")]
    private partial void LogAffiliationUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Admission recheck failed")]
    private partial void LogRecheckFailed(Exception exception);
}
