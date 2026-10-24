using CommunityToolkit.Mvvm.ComponentModel;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// The search box on the map: systems matching what is typed (<see cref="SystemSearch"/>), with one
/// highlighted for the keyboard. Choosing one hands its index to the map, which selects it and goes there.
/// </summary>
public sealed partial class SystemSearchViewModel(Func<int, MapSystemInfo> describe, Action<int> choose) : ObservableObject
{
    private MapModel? _model;

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    public partial IReadOnlyList<MapSystemInfo> Results { get; private set; } = [];

    /// <summary>The result Enter chooses, moved with the arrow keys.</summary>
    [ObservableProperty]
    public partial MapSystemInfo? Highlighted { get; set; }

    public bool HasResults => Results.Count > 0;

    /// <summary>The map to search; null while none is loaded.</summary>
    public void SetModel(MapModel? model)
    {
        _model = model;
        Clear();
    }

    /// <summary>Moves the highlight down (1) or up (-1) the results, wrapping around.</summary>
    public void Move(int step)
    {
        if (Results.Count == 0)
            return;

        var at = Highlighted is { } h ? IndexOf(h) : -1;
        Highlighted = Results[((at + step) % Results.Count + Results.Count) % Results.Count];
    }

    /// <summary>Chooses the highlighted result (Enter); false when there is none.</summary>
    public bool ChooseHighlighted()
    {
        if (Highlighted is not { } result)
            return false;

        Choose(result);
        return true;
    }

    public void Choose(MapSystemInfo result)
    {
        choose(result.Index);
        Clear();
    }

    public void Clear() => Query = "";

    partial void OnQueryChanged(string value)
    {
        Results = _model is { } model ? SystemSearch.Find(model, value).Select(describe).ToList() : [];
        Highlighted = Results.FirstOrDefault();
    }

    private int IndexOf(MapSystemInfo result)
    {
        for (var i = 0; i < Results.Count; i++)
        {
            if (Results[i].Index == result.Index)
                return i;
        }

        return -1;
    }
}
