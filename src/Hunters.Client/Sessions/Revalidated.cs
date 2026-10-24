using System.Net.Http.Headers;

namespace NewEdenSoft.Hunters.Client.Sessions;

/// <summary>An answer from the server with its ETag, to ask again with (<c>If-None-Match</c>).</summary>
public sealed record Revalidated<T>(T Value, EntityTagHeaderValue? ETag);
