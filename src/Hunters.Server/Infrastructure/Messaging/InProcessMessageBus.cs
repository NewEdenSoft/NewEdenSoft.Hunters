using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace NewEdenSoft.Hunters.Server.Infrastructure.Messaging;

/// <summary>
/// Single-instance <see cref="IMessageBus"/>. Handlers run in subscription order; a failing
/// handler is logged and does not stop delivery to the others.
/// </summary>
public sealed partial class InProcessMessageBus(ILogger<InProcessMessageBus> logger) : IMessageBus
{
    private readonly ConcurrentDictionary<string, ImmutableArray<Subscription>> _channels = new();

    public async ValueTask PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(channel, out var subscriptions))
            return;

        foreach (var subscription in subscriptions)
        {
            if (subscription.Handler is not Func<T, CancellationToken, ValueTask> handler)
                continue;

            try
            {
                await handler(message, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogHandlerFailed(ex, channel);
            }
        }
    }

    public IDisposable Subscribe<T>(string channel, Func<T, CancellationToken, ValueTask> handler)
    {
        var subscription = new Subscription(this, channel, handler);
        _channels.AddOrUpdate(channel, [subscription], (_, existing) => existing.Add(subscription));
        return subscription;
    }

    private void Unsubscribe(Subscription subscription)
    {
        while (_channels.TryGetValue(subscription.Channel, out var existing))
        {
            var updated = existing.Remove(subscription);
            var replaced = updated.IsEmpty
                ? _channels.TryRemove(new KeyValuePair<string, ImmutableArray<Subscription>>(subscription.Channel, existing))
                : _channels.TryUpdate(subscription.Channel, updated, existing);
            if (replaced)
                return;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Message handler on channel {Channel} failed")]
    private partial void LogHandlerFailed(Exception exception, string channel);

    private sealed class Subscription(InProcessMessageBus bus, string channel, Delegate handler) : IDisposable
    {
        public string Channel { get; } = channel;
        public Delegate Handler { get; } = handler;

        public void Dispose() => bus.Unsubscribe(this);
    }
}
