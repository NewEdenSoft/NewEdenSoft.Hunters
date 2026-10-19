using Microsoft.Extensions.Logging.Abstractions;
using NewEdenSoft.Hunters.Server.Infrastructure.Messaging;

namespace NewEdenSoft.Hunters.Server.Tests.Infrastructure;

public sealed class InProcessMessageBusTests
{
    private readonly InProcessMessageBus _bus = new(NullLogger<InProcessMessageBus>.Instance);

    [Fact]
    public async Task Delivers_to_subscribers_of_the_channel_only()
    {
        var received = new List<string>();
        using var _ = _bus.Subscribe<string>("fleet:1", (m, _) => { received.Add(m); return ValueTask.CompletedTask; });
        using var __ = _bus.Subscribe<string>("fleet:2", (m, _) => { received.Add("wrong:" + m); return ValueTask.CompletedTask; });

        await _bus.PublishAsync("fleet:1", "hello");

        Assert.Equal(["hello"], received);
    }

    [Fact]
    public async Task Stops_delivering_after_dispose()
    {
        var count = 0;
        var subscription = _bus.Subscribe<int>("c", (_, _) => { count++; return ValueTask.CompletedTask; });

        await _bus.PublishAsync("c", 1);
        subscription.Dispose();
        await _bus.PublishAsync("c", 2);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Failing_handler_does_not_block_others()
    {
        var delivered = false;
        using var _ = _bus.Subscribe<int>("c", (_, _) => throw new InvalidOperationException("boom"));
        using var __ = _bus.Subscribe<int>("c", (_, _) => { delivered = true; return ValueTask.CompletedTask; });

        await _bus.PublishAsync("c", 1);

        Assert.True(delivered);
    }
}
