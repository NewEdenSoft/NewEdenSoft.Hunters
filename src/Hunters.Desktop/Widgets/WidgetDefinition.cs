namespace NewEdenSoft.Hunters.Desktop.Widgets;

/// <summary>Static description of a widget, registered with <see cref="WidgetRegistrationExtensions.AddWidget{TViewModel, TView}"/>.</summary>
public sealed record WidgetDefinition(
    string Id,
    string Title,
    string Description,
    Type ViewModelType,
    double DefaultWidth,
    double DefaultHeight,
    double MinWidth = 200,
    double MinHeight = 120);
