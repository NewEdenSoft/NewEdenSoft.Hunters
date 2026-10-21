using System.Text.Json;

namespace NewEdenSoft.Hunters.Client.Http;

/// <summary>The <c>code</c> and <c>title</c> of a problem response from a Hunters server.</summary>
internal sealed record ServerProblem(string? Code, string? Title)
{
    public static async Task<ServerProblem> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = problem.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
                return new ServerProblem(String(root, "code"), String(root, "title"));
        }
        catch (JsonException)
        {
            // Not a problem response.
        }

        return new ServerProblem(null, null);

        static string? String(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
