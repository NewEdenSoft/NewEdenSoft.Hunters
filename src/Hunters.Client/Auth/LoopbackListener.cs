using System.Net;
using System.Net.Sockets;
using System.Text;
using NewEdenSoft.Hunters.Contracts.Auth;

namespace NewEdenSoft.Hunters.Client.Auth;

/// <summary>
/// Minimal HTTP listener on <c>127.0.0.1</c> that waits for the server's redirect to
/// <see cref="AuthPaths.LoopbackCallback"/> at the end of a desktop login. Uses a plain socket, so
/// no URL reservation or admin rights are needed. Other requests (favicon, browser preconnects) are
/// answered with 404 or ignored.
/// </summary>
public sealed class LoopbackListener : IAsyncDisposable
{
    private const int MaxRequestBytes = 16 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener;

    private LoopbackListener(TcpListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    /// <summary>Starts listening on a free port chosen by the OS.</summary>
    public static LoopbackListener Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new LoopbackListener(listener);
    }

    /// <summary>
    /// Waits for the callback request, answers it with the page from <paramref name="renderPage"/>
    /// and returns its query parameters.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> WaitForCallbackAsync(
        Func<IReadOnlyDictionary<string, string>, string> renderPage, CancellationToken cancellationToken)
    {
        var result = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var _ = stop.Token.Register(() => result.TrySetCanceled(cancellationToken));

        var connections = new List<Task>();
        var accepting = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                // Browsers open spare connections that may never send a request, so each one is handled on its own.
                lock (connections)
                    connections.Add(HandleAsync(client, renderPage, result, stop.Token));
            }
        }, CancellationToken.None);

        try
        {
            return await result.Task;
        }
        finally
        {
            await stop.CancelAsync();
            await accepting;
            Task[] pending;
            lock (connections)
                pending = [.. connections];
            await Task.WhenAll(pending);
        }
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private static async Task HandleAsync(
        TcpClient client,
        Func<IReadOnlyDictionary<string, string>, string> renderPage,
        TaskCompletionSource<IReadOnlyDictionary<string, string>> result,
        CancellationToken cancellationToken)
    {
        using var _ = client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);

        try
        {
            var stream = client.GetStream();
            var target = await ReadRequestTargetAsync(stream, timeout.Token);
            if (target is null)
                return;

            var queryStart = target.IndexOf('?');
            var path = queryStart < 0 ? target : target[..queryStart];
            if (path != AuthPaths.LoopbackCallback || result.Task.IsCompleted)
            {
                await WriteResponseAsync(stream, "404 Not Found", "", timeout.Token);
                return;
            }

            var query = ParseQuery(queryStart < 0 ? "" : target[(queryStart + 1)..]);
            await WriteResponseAsync(stream, "200 OK", renderPage(query), timeout.Token);
            result.TrySetResult(query);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
        {
            // A dropped or idle connection is not the callback; keep waiting.
        }
    }

    /// <summary>Reads the request head and returns the target of a GET request line, or null.</summary>
    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
                return null;
            length += read;
            if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0)
                break;
        }

        var head = Encoding.ASCII.GetString(buffer, 0, length);
        var requestLine = head[..Math.Max(0, head.IndexOf("\r\n", StringComparison.Ordinal))].Split(' ');
        return requestLine is ["GET", var target, _] ? target : null;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string status, string html, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = Decode(separator < 0 ? pair : pair[..separator]);
            values.TryAdd(key, separator < 0 ? "" : Decode(pair[(separator + 1)..]));
        }

        return values;

        static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
    }
}
