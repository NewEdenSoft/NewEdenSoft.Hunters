using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel.Data;

namespace NewEdenSoft.Hunters.Intel.Kills;

/// <summary>zKillboard's R2Z2 feed: one file per kill, numbered by a sequence.</summary>
public interface IKillFeedSource
{
    /// <summary>
    /// The newest sequence number zKillboard has announced. It lags a little behind the files that
    /// exist, so it only tells a missing file (at or below it) from one not written yet (above it).
    /// </summary>
    Task<long> GetLatestSequenceAsync(CancellationToken cancellationToken);

    /// <summary>The file for <paramref name="sequence"/>, or null when there is none (not yet, or gone after about a day).</summary>
    Task<KillFeedFile?> GetAsync(long sequence, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IKillFeedSource"/> over HTTP (<c>r2z2.zkillboard.com/ephemeral/</c>). zKillboard asks for a
/// <c>User-Agent</c> with a contact, at most 15 requests a second, and 6 seconds after a 404 (the reader
/// keeps to those). Other failures throw <see cref="HttpRequestException"/>.
/// </summary>
public sealed class R2Z2KillFeedSource(HttpClient http) : IKillFeedSource
{
    public async Task<long> GetLatestSequenceAsync(CancellationToken cancellationToken)
    {
        var latest = await http.GetFromJsonAsync<SequenceFile>("sequence.json", cancellationToken);
        return latest?.Sequence ?? throw new HttpRequestException("R2Z2 sent an empty sequence.json.");
    }

    public async Task<KillFeedFile?> GetAsync(long sequence, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"{sequence}.json", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<KillFeedFile>(cancellationToken)
            ?? throw new HttpRequestException($"R2Z2 sent an empty file for sequence {sequence}.");
    }

    private sealed record SequenceFile([property: JsonPropertyName("sequence")] long Sequence);

    /// <summary>Registers the source with its base address and the contact in the <c>User-Agent</c>.</summary>
    public static void Configure(IServiceProvider services, HttpClient http)
    {
        var options = services.GetRequiredService<IOptions<IntelOptions>>().Value;
        http.BaseAddress = options.Kills.FeedUrl;
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"NewEdenSoft.Hunters.Intel (+{options.Esi.Contact})");
    }
}

/// <summary>One R2Z2 file: the ESI killmail and zKillboard's notes on it. Only the fields intel reads.</summary>
public sealed record KillFeedFile(
    [property: JsonPropertyName("killmail_id")] long KillmailId,
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("sequence_id")] long SequenceId,
    [property: JsonPropertyName("esi")] FeedKillmail Esi,
    [property: JsonPropertyName("zkb")] FeedZkb Zkb)
{
    /// <summary>Item flags of fitted modules: low, mid and high slots, rigs and subsystems.</summary>
    private static bool Fitted(int flag) => flag is >= 11 and <= 34 or >= 92 and <= 99 or >= 125 and <= 132;

    /// <summary>The slim row stored for this kill.</summary>
    public Kill ToKill() => new()
    {
        KillmailId = KillmailId,
        Hash = Hash,
        Sequence = SequenceId,
        Time = Esi.KillmailTime,
        SystemId = Esi.SolarSystemId,
        Npc = Zkb.Npc,
        Solo = Zkb.Solo,
        TotalValue = Zkb.TotalValue,
        VictimCharacterId = Esi.Victim.CharacterId,
        VictimCorporationId = Esi.Victim.CorporationId,
        VictimAllianceId = Esi.Victim.AllianceId,
        VictimShipTypeId = Esi.Victim.ShipTypeId,
        VictimFittedTypeIds = [.. (Esi.Victim.Items ?? []).Where(i => Fitted(i.Flag)).Select(i => i.ItemTypeId).Distinct().Order()],
        AttackerCount = Esi.Attackers.Count,
        AttackerCharacterIds = [.. Esi.Attackers.Select(a => a.CharacterId ?? 0)],
        AttackerCorporationIds = [.. Esi.Attackers.Select(a => a.CorporationId ?? 0)],
        AttackerAllianceIds = [.. Esi.Attackers.Select(a => a.AllianceId ?? 0)],
        AttackerShipTypeIds = [.. Esi.Attackers.Select(a => a.ShipTypeId ?? 0)],
        AttackerWeaponTypeIds = [.. Esi.Attackers.Select(a => a.WeaponTypeId ?? 0)],
    };
}

public sealed record FeedKillmail(
    [property: JsonPropertyName("killmail_time")] DateTimeOffset KillmailTime,
    [property: JsonPropertyName("solar_system_id")] int SolarSystemId,
    [property: JsonPropertyName("victim")] FeedVictim Victim,
    [property: JsonPropertyName("attackers")] IReadOnlyList<FeedAttacker> Attackers);

public sealed record FeedVictim(
    [property: JsonPropertyName("character_id")] long? CharacterId,
    [property: JsonPropertyName("corporation_id")] long? CorporationId,
    [property: JsonPropertyName("alliance_id")] long? AllianceId,
    [property: JsonPropertyName("ship_type_id")] int ShipTypeId,
    [property: JsonPropertyName("items")] IReadOnlyList<FeedItem>? Items);

public sealed record FeedItem(
    [property: JsonPropertyName("item_type_id")] int ItemTypeId,
    [property: JsonPropertyName("flag")] int Flag);

public sealed record FeedAttacker(
    [property: JsonPropertyName("character_id")] long? CharacterId,
    [property: JsonPropertyName("corporation_id")] long? CorporationId,
    [property: JsonPropertyName("alliance_id")] long? AllianceId,
    [property: JsonPropertyName("ship_type_id")] int? ShipTypeId,
    [property: JsonPropertyName("weapon_type_id")] int? WeaponTypeId);

public sealed record FeedZkb(
    [property: JsonPropertyName("totalValue")] double TotalValue,
    [property: JsonPropertyName("npc")] bool Npc,
    [property: JsonPropertyName("solo")] bool Solo);
