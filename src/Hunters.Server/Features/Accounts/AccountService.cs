using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Server.Features.Admission;
using NewEdenSoft.Hunters.Server.Features.Auth;
using NewEdenSoft.Hunters.Server.Configuration;
using NewEdenSoft.Hunters.Server.Infrastructure.Persistence;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Features.Accounts;

public enum AccountChangeResult
{
    Done,
    NotFound,
    Rejected,
}

/// <summary>Account and character management for the signed-in user.</summary>
public sealed class AccountService(
    HuntersDbContext db,
    SessionService sessions,
    AdmissionService admission,
    IEveSsoClient sso,
    TokenProtector protector,
    IOptionsSnapshot<HuntersOptions> options)
{
    public async Task<AccountInfo?> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .Include(a => a.Characters)
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        return account is null ? null : ToInfo(account);
    }

    public AccountInfo ToInfo(Account account)
    {
        var required = options.Value.Sso.Scopes;
        var characters = account.Characters
            .OrderByDescending(c => c.Id == account.MainCharacterId)
            .ThenBy(c => c.AddedAt)
            .Select(c =>
            {
                var missing = required.Except(c.Scopes).ToList();
                var status = c.Status == CharacterStatus.Active && missing.Count > 0 ? CharacterStatus.MissingScopes : c.Status;
                return new CharacterInfo(c.Id, c.Name, c.Id == account.MainCharacterId, status, missing, c.AddedAt);
            })
            .ToList();

        var admins = options.Value.AdminCharacterIds;
        var isAdmin = account.Characters.Any(c => admins.Contains(c.Id));
        return new AccountInfo(account.Id, account.MainCharacterId, isAdmin, characters);
    }

    public async Task<bool> IsAdminAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var admins = options.Value.AdminCharacterIds;
        return admins.Length > 0
            && await db.Characters.AnyAsync(c => c.AccountId == accountId && admins.Contains(c.Id), cancellationToken);
    }

    /// <summary>Changes the main character. With first-character admission the new main must qualify.</summary>
    public async Task<(AccountChangeResult Result, string? Reason)> SetMainAsync(Guid accountId, long characterId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null || !await db.Characters.AnyAsync(c => c.AccountId == accountId && c.Id == characterId, cancellationToken))
            return (AccountChangeResult.NotFound, null);

        if (options.Value.Admission.Affiliation.Scope == AffiliationScope.FirstCharacter && admission.HasCombinableRules)
        {
            var check = await admission.CheckAsync(characterId, characterId, cancellationToken);
            if (!check.Allowed)
                return (AccountChangeResult.Rejected, check.Reason);
        }

        account.MainCharacterId = characterId;
        await db.SaveChangesAsync(cancellationToken);
        return (AccountChangeResult.Done, null);
    }

    /// <summary>Revokes the character's token at EVE and removes it. The main can only go when it is the last character.</summary>
    public async Task<(AccountChangeResult Result, string? Reason)> RemoveCharacterAsync(Guid accountId, long characterId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.Include(a => a.Characters).FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        var character = account?.Characters.FirstOrDefault(c => c.Id == characterId);
        if (account is null || character is null)
            return (AccountChangeResult.NotFound, null);

        if (account.MainCharacterId == characterId && account.Characters.Count > 1)
            return (AccountChangeResult.Rejected, "Choose another main character before removing this one.");

        await RevokeAtEveAsync(character, cancellationToken);
        db.Characters.Remove(character);
        if (account.MainCharacterId == characterId)
            account.MainCharacterId = null;

        await db.SaveChangesAsync(cancellationToken);
        return (AccountChangeResult.Done, null);
    }

    /// <summary>Revokes every token at EVE, ends every session and deletes the account.</summary>
    public async Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.Include(a => a.Characters).FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
            return false;

        foreach (var character in account.Characters)
            await RevokeAtEveAsync(character, cancellationToken);

        await sessions.RevokeAllAsync(accountId, cancellationToken);
        db.Accounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// The character was transferred to another player (owner hash changed): remove it from its
    /// old account. Does not save; the caller saves with its own changes.
    /// </summary>
    public async Task UnlinkTransferredAsync(Character character, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.Include(a => a.Characters).FirstAsync(a => a.Id == character.AccountId, cancellationToken);
        account.Characters.Remove(character);
        db.Characters.Remove(character);

        if (account.MainCharacterId == character.Id)
            account.MainCharacterId = account.Characters.OrderBy(c => c.AddedAt).FirstOrDefault()?.Id;
    }

    private async Task RevokeAtEveAsync(Character character, CancellationToken cancellationToken)
    {
        if (protector.Unprotect(character.EncryptedRefreshToken) is { } refreshToken)
            await sso.RevokeAsync(refreshToken, cancellationToken);
    }
}
