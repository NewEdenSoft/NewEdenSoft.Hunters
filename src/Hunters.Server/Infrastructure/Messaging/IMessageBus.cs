namespace NewEdenSoft.Hunters.Server.Infrastructure.Messaging;

/// <summary>
/// Publish/subscribe between server instances. With one instance it is in-process; a distributed
/// implementation (for example Redis pub/sub) can replace it. Messages must be serializable.
/// Delivery is at-most-once: subscribers must tolerate missed messages.
/// </summary>
public interface IMessageBus
{
    ValueTask PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to a channel until the returned handle is disposed.</summary>
    IDisposable Subscribe<T>(string channel, Func<T, CancellationToken, ValueTask> handler);
}
