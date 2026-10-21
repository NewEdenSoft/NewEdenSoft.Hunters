using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using NewEdenSoft.Hunters.Contracts.Admin;
using NewEdenSoft.Hunters.Server.Features.Accounts;
using NewEdenSoft.Hunters.Server.Features.Auth;
using NewEdenSoft.Hunters.Server.Infrastructure.Persistence;

namespace NewEdenSoft.Hunters.Server.Features.Admin;

/// <summary>Server administration: access keys and accounts. Admins are configured by character id.</summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("")
            .RequireAuthorization()
            .AddEndpointFilter(RequireAdminAsync)
            .WithTags("Admin");

        admin.MapGet(AdminPaths.AccessKeys, async (HuntersDbContext db, CancellationToken ct) =>
            (await db.AccessKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(ct)).Select(ToInfo).ToList());

        admin.MapPost(AdminPaths.AccessKeys, async (CreateAccessKeyRequest request, HuntersDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Label))
                return Results.Problem(title: "Give the access key a label.", statusCode: StatusCodes.Status400BadRequest);

            var key = Secrets.NewToken("hk_");
            var entity = new AccessKey
            {
                Id = Guid.CreateVersion7(),
                Label = request.Label.Trim(),
                KeyHash = Secrets.Sha256(key),
                CreatedAt = time.GetUtcNow(),
                ExpiresAt = request.ExpiresAt,
                MaxUses = request.MaxUses,
            };
            db.AccessKeys.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new CreatedAccessKey(ToInfo(entity), key));
        });

        admin.MapDelete(AdminPaths.AccessKeys + "/{id:guid}", async (Guid id, HuntersDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var key = await db.AccessKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
            if (key is null)
                return Results.NotFound();
            key.RevokedAt ??= time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        admin.MapGet(AdminPaths.Accounts, async (HuntersDbContext db, CancellationToken ct) =>
            await db.Accounts.AsNoTracking()
                .OrderBy(a => a.CreatedAt)
                .Select(a => new AdminAccountInfo(
                    a.Id,
                    a.MainCharacterId,
                    a.Characters.Where(c => c.Id == a.MainCharacterId).Select(c => c.Name).FirstOrDefault(),
                    a.Characters.Count,
                    a.Sessions.Count(s => s.RevokedAt == null),
                    a.IsDisabled,
                    a.CreatedAt))
                .ToListAsync(ct));

        admin.MapPost(AdminPaths.Accounts + "/{id:guid}/disable", (Guid id, HuntersDbContext db, SessionService sessions, CancellationToken ct) =>
            SetDisabledAsync(id, true, db, sessions, ct));

        admin.MapPost(AdminPaths.Accounts + "/{id:guid}/enable", (Guid id, HuntersDbContext db, SessionService sessions, CancellationToken ct) =>
            SetDisabledAsync(id, false, db, sessions, ct));

        admin.MapDelete(AdminPaths.Accounts + "/{id:guid}/sessions", async (Guid id, SessionService sessions, CancellationToken ct) =>
            Results.Ok(new { revoked = await sessions.RevokeAllAsync(id, ct) }));

        return endpoints;
    }

    private static async ValueTask<object?> RequireAdminAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var session = http.User.GetSession();
        var accounts = http.RequestServices.GetRequiredService<AccountService>();
        if (session is null || !await accounts.IsAdminAsync(session.AccountId, http.RequestAborted))
            return Results.Forbid();
        return await next(context);
    }

    private static async Task<IResult> SetDisabledAsync(Guid id, bool disabled, HuntersDbContext db, SessionService sessions, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null)
            return Results.NotFound();

        account.IsDisabled = disabled;
        await db.SaveChangesAsync(ct);
        if (disabled)
            await sessions.RevokeAllAsync(id, ct);
        return Results.NoContent();
    }

    private static AccessKeyInfo ToInfo(AccessKey k) =>
        new(k.Id, k.Label, k.CreatedAt, k.ExpiresAt, k.MaxUses, k.Uses, k.RevokedAt is not null);
}
