using System.Net;
using System.Net.Http.Json;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Admin;
using NewEdenSoft.Hunters.Server.Tests.Features.Auth;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Admin;

[Collection(PostgresCollection.Name)]
public sealed class AdminTests : IDisposable
{
    private readonly long _adminId = TestIds.Character();

    public AdminTests(PostgresFixture postgres)
    {
        Factory = new HuntersServerFactory()
            .WithDatabase(postgres.ConnectionString)
            .With("Hunters:Admins:0", _adminId.ToString());
    }

    private HuntersServerFactory Factory { get; }

    public void Dispose() => Factory.Dispose();

    [Fact]
    public async Task Non_admins_are_forbidden()
    {
        var driver = new LoginDriver(Factory);
        var user = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "User"));

        Assert.False(user.Account.IsAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.AuthorizedClient(user.SessionToken).GetAsync(AdminPaths.AccessKeys)).StatusCode);
    }

    [Fact]
    public async Task Admin_creates_lists_and_revokes_access_keys()
    {
        var driver = new LoginDriver(Factory);
        var admin = await driver.DesktopLoginAsync(FakeEveSso.Character(_adminId, "Admin"));
        var client = driver.AuthorizedClient(admin.SessionToken);
        Assert.True(admin.Account.IsAdmin);

        var created = await (await client.PostAsJsonAsync(AdminPaths.AccessKeys, new CreateAccessKeyRequest("Corp"))).Content.ReadFromJsonAsync<CreatedAccessKey>();
        Assert.StartsWith("hk_", created!.Key);

        Assert.Contains(await client.GetFromJsonAsync<List<AccessKeyInfo>>(AdminPaths.AccessKeys) ?? [], k => k.Id == created.AccessKey.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(AdminPaths.AccessKey(created.AccessKey.Id))).StatusCode);
        var keys = await client.GetFromJsonAsync<List<AccessKeyInfo>>(AdminPaths.AccessKeys);
        Assert.True(keys!.Single(k => k.Id == created.AccessKey.Id).IsRevoked);
    }

    [Fact]
    public async Task Disabling_an_account_ends_its_sessions_and_blocks_login()
    {
        var driver = new LoginDriver(Factory);
        var admin = await driver.DesktopLoginAsync(FakeEveSso.Character(_adminId, "Admin"));
        var userIdentity = FakeEveSso.Character(TestIds.Character(), "Troublemaker");
        var user = await driver.DesktopLoginAsync(userIdentity);
        var adminClient = driver.AuthorizedClient(admin.SessionToken);

        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.PostAsync(AdminPaths.AccountDisable(user.Account.Id), null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await driver.AuthorizedClient(user.SessionToken).GetAsync(AccountPaths.Account)).StatusCode);
        Assert.Equal(Contracts.Auth.AuthErrorCodes.AccountDisabled, (await driver.DesktopUntilLoopbackAsync(userIdentity)).Error);

        var accounts = await adminClient.GetFromJsonAsync<List<AdminAccountInfo>>(AdminPaths.Accounts);
        Assert.True(accounts!.Single(a => a.Id == user.Account.Id).IsDisabled);
    }
}
