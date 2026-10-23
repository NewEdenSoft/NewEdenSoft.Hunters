namespace NewEdenSoft.Hunters.Contracts.Discovery;

/// <summary>
/// Served anonymously at <see cref="DiscoveryDocument.Path"/>. Tells a client what a server is and
/// what it requires before the client connects.
/// </summary>
public sealed record DiscoveryDocument(
    string Name,
    string ApiVersion,
    string MinClientVersion,
    AdmissionInfo Admission,
    MapPackInfo? MapPack,
    IReadOnlyList<string> Features)
{
    public const string Path = "/.well-known/hunters";
}

/// <summary>What a client must provide to log in. Never reveals the server's allow lists.</summary>
public sealed record AdmissionInfo(bool RequiresAccessKey);

/// <summary>The map pack currently served, identified by the SDE build it was generated from.</summary>
public sealed record MapPackInfo(long SdeBuild);

/// <summary>Values of <see cref="DiscoveryDocument.Features"/>.</summary>
public static class HuntersFeatures
{
    /// <summary>The server has activity data to serve (<c>/api/activity/*</c>).</summary>
    public const string Activity = "activity";
}
