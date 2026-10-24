using System.Net;
using System.Net.Http.Json;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Activity;

namespace NewEdenSoft.Hunters.Desktop.Tests.Account;

public sealed class RevalidationTests
{
    [Fact]
    public async Task Activity_is_asked_again_with_its_etag_and_kept_on_304()
    {
        var server = new ActivityServer();
        var session = new HuntersSession(new HttpClient(server), new Uri("https://hunters.example"),
            new DesktopLoginResult("token", Guid.NewGuid(), false, 1, new AccountInfo(Guid.NewGuid(), null, false, [])));

        var first = await session.GetActivityAsync(null);
        var second = await session.GetActivityAsync(first);
        server.Version = 2;
        var third = await session.GetActivityAsync(second);

        Assert.Equal([null, "\"1\"", "\"1\""], server.IfNoneMatch);
        Assert.Same(first, second);
        Assert.Equal(2, third.Value.Version);
    }

    private sealed class ActivityServer : HttpMessageHandler
    {
        public long Version { get; set; } = 1;

        public List<string?> IfNoneMatch { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sent = request.Headers.IfNoneMatch.FirstOrDefault()?.Tag;
            IfNoneMatch.Add(sent);
            var etag = $"\"{Version}\"";
            var response = sent == etag
                ? new HttpResponseMessage(HttpStatusCode.NotModified)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Activity(Version)) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
            return Task.FromResult(response);
        }

        private static ActivityCurrent Activity(long version)
        {
            var columns = new ActivityColumns([], null, [], null, [], [], []);
            var metrics = new ActivityMetrics(columns, columns, columns, columns);
            return new ActivityCurrent(version, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 24, 24, metrics, metrics);
        }
    }
}
