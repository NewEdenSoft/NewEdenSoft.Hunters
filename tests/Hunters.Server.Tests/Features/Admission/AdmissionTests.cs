using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Contracts.Admin;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Features.Admission;
using NewEdenSoft.Hunters.Server.Tests.Features.Auth;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Admission;

[Collection(PostgresCollection.Name)]
public sealed class AdmissionTests(PostgresFixture postgres)
{
    private const long AllowedCorp = 98000001;

    private HuntersServerFactory NewFactory() => new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

    [Fact]
    public async Task Access_key_is_required_for_new_accounts()
    {
        using var factory = NewFactory().With("Hunters:Admission:AccessKey:Enabled", "true");
        var driver = new LoginDriver(factory);

        var attempt = await driver.DesktopUntilLoopbackAsync(FakeEveSso.Character(TestIds.Character(), "No Key"));

        Assert.Equal(AuthErrorCodes.AccessKeyRequired, attempt.Error);
    }

    [Fact]
    public async Task Valid_access_key_creates_the_account_and_counts_a_use()
    {
        var adminId = TestIds.Character();
        using var factory = NewFactory()
            .With("Hunters:Admission:AccessKey:Enabled", "true")
            .With("Hunters:Admins:0", adminId.ToString());
        var driver = new LoginDriver(factory);

        // Bootstrap: the admin gets in with a key created directly through the admin API of a
        // server without the requirement, then the requirement applies to everyone else.
        using var open = NewFactory().With("Hunters:Admins:0", adminId.ToString());
        var admin = await new LoginDriver(open).DesktopLoginAsync(FakeEveSso.Character(adminId, "Admin"));
        var created = await (await new LoginDriver(open).AuthorizedClient(admin.SessionToken)
            .PostAsJsonAsync(AdminPaths.AccessKeys, new CreateAccessKeyRequest("Friends", MaxUses: 1)))
            .Content.ReadFromJsonAsync<CreatedAccessKey>();

        var first = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Friend"), accessKey: created!.Key);
        Assert.True(first.IsNewAccount);

        // MaxUses = 1: the key is now used up.
        var client = factory.CreateClient();
        var begin = await client.PostAsJsonAsync(AuthPaths.DesktopBegin, new BeginDesktopLoginRequest(50000, new string('a', 43), created.Key));
        Assert.Equal(HttpStatusCode.BadRequest, begin.StatusCode);
        Assert.Contains(AuthErrorCodes.AccessKeyInvalid, await begin.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Existing_accounts_log_in_without_an_access_key()
    {
        var identity = FakeEveSso.Character(TestIds.Character(), "Veteran");
        using (var open = NewFactory())
            await new LoginDriver(open).DesktopLoginAsync(identity);

        using var locked = NewFactory().With("Hunters:Admission:AccessKey:Enabled", "true");
        var again = await new LoginDriver(locked).DesktopLoginAsync(identity);

        Assert.False(again.IsNewAccount);
    }

    [Fact]
    public async Task Affiliation_rule_admits_members_and_refuses_others()
    {
        using var factory = NewFactory()
            .With("Hunters:Admission:Affiliation:Enabled", "true")
            .With("Hunters:Admission:Affiliation:CorporationIds:0", AllowedCorp.ToString());
        var driver = new LoginDriver(factory);
        var member = TestIds.Character();
        var outsider = TestIds.Character();
        factory.Affiliations.Set(member, AllowedCorp);
        factory.Affiliations.Set(outsider, 1000001);

        Assert.True((await driver.DesktopLoginAsync(FakeEveSso.Character(member, "Member"))).IsNewAccount);
        Assert.Equal(AuthErrorCodes.AdmissionDenied, (await driver.DesktopUntilLoopbackAsync(FakeEveSso.Character(outsider, "Outsider"))).Error);
    }

    [Fact]
    public async Task Affiliation_rule_fails_closed_when_esi_is_down()
    {
        using var factory = NewFactory()
            .With("Hunters:Admission:Affiliation:Enabled", "true")
            .With("Hunters:Admission:Affiliation:CorporationIds:0", AllowedCorp.ToString());
        factory.Affiliations.Unavailable = true;

        var attempt = await new LoginDriver(factory).DesktopUntilLoopbackAsync(FakeEveSso.Character(TestIds.Character(), "Member"));

        Assert.Equal(AuthErrorCodes.AdmissionDenied, attempt.Error);
    }

    [Fact]
    public async Task Deny_list_always_wins()
    {
        var denied = TestIds.Character();
        using var factory = NewFactory().With("Hunters:Admission:Characters:Deny:0", denied.ToString());

        var attempt = await new LoginDriver(factory).DesktopUntilLoopbackAsync(FakeEveSso.Character(denied, "Banned"));

        Assert.Equal(AuthErrorCodes.AdmissionDenied, attempt.Error);
    }

    [Fact]
    public async Task Recheck_revokes_sessions_of_accounts_that_left_the_corporation()
    {
        using var factory = NewFactory()
            .With("Hunters:Admission:Affiliation:Enabled", "true")
            .With("Hunters:Admission:Affiliation:CorporationIds:0", AllowedCorp.ToString());
        var driver = new LoginDriver(factory);
        var id = TestIds.Character();
        factory.Affiliations.Set(id, AllowedCorp);
        var login = await driver.DesktopLoginAsync(FakeEveSso.Character(id, "Leaver"));

        factory.Affiliations.Set(id, 1000001);
        var recheck = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<AdmissionRecheckService>().Single();
        await recheck.RecheckAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, (await driver.AuthorizedClient(login.SessionToken).GetAsync("/api/account")).StatusCode);
    }
}
