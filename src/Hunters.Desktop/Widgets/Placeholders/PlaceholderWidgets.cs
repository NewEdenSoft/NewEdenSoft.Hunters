namespace NewEdenSoft.Hunters.Desktop.Widgets.Placeholders;

/// <summary>Empty-state content for widgets whose features are not built yet.</summary>
public abstract class PlaceholderWidgetViewModel : WidgetViewModelBase
{
    public abstract string EmptyTitle { get; }

    public abstract string EmptyMessage { get; }
}

public sealed class NearbyActivityWidgetViewModel : PlaceholderWidgetViewModel
{
    public override string EmptyTitle => "No activity data yet";

    public override string EmptyMessage => "Kills and jumps near your characters will appear here.";
}

public sealed class FleetWidgetViewModel : PlaceholderWidgetViewModel
{
    public override string EmptyTitle => "Not in a fleet session";

    public override string EmptyMessage => "Fleet members, their locations and broadcast destinations will appear here.";
}
