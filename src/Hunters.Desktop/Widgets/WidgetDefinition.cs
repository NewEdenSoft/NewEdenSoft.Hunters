namespace NewEdenSoft.Hunters.Desktop.Widgets;

/// <summary>Static description of a widget, registered with <see cref="WidgetRegistrationExtensions.AddWidget{TViewModel, TView}"/>.</summary>
/// <param name="FillsPage">Docked, the widget fills the height of its page instead of scrolling with it (the map).</param>
public sealed record WidgetDefinition(
    string Id,
    string Title,
    string Description,
    Type ViewModelType,
    double DefaultWidth,
    double DefaultHeight,
    double MinWidth = 200,
    double MinHeight = 120,
    bool FillsPage = false);
