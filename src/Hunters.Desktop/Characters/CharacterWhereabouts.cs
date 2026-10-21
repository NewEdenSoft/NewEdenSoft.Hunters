using NewEdenSoft.Hunters.Client.Tracking;

namespace NewEdenSoft.Hunters.Desktop.Characters;

/// <summary>
/// A tracked character's place and ship as text, for the Account page and the Characters widget.
/// Names come from <see cref="EveNames"/>; call <see cref="ResolveNamesAsync"/> first.
/// </summary>
/// <param name="System">The solar system, e.g. "Jita".</param>
/// <param name="Docked">"Docked at {station}", "Docked" in a player structure (not public), or null in space.</param>
/// <param name="Ship">The ship type, with the ship's own name when it has one: "Ishtar “Hunter One”".</param>
public sealed record CharacterWhereabouts(string System, string? Docked, string? Ship)
{
    /// <summary>Null until the tracker knows where the character is.</summary>
    public static CharacterWhereabouts? From(TrackedCharacter? state, EveNames names)
    {
        if (state is not { Location: { } location })
            return null;

        var docked = location.IsDocked
            ? location.StationId is { } station && names.Get(station) is { } stationName ? $"Docked at {stationName}" : "Docked"
            : null;

        string? ship = null;
        if (state.Ship is { } s)
        {
            var type = names.Get(s.TypeId);
            ship = type is null ? s.Name : s.Name == type || string.IsNullOrWhiteSpace(s.Name) ? type : $"{type} “{s.Name}”";
        }

        return new CharacterWhereabouts(names.Get(location.SolarSystemId) ?? "Unknown system", docked, ship);
    }

    /// <summary>Looks up the names a state needs. Player structures are not public, so they are skipped.</summary>
    public static Task ResolveNamesAsync(TrackedCharacter? state, EveNames names)
    {
        if (state is not { Location: { } location })
            return Task.CompletedTask;

        long?[] ids = [location.SolarSystemId, location.StationId, state.Ship?.TypeId];
        return names.ResolveAsync(ids.OfType<long>());
    }
}
