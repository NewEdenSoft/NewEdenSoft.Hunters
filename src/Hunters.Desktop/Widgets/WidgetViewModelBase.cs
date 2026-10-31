using CommunityToolkit.Mvvm.ComponentModel;
using NewEdenSoft.Hunters.Desktop.ViewModels;

namespace NewEdenSoft.Hunters.Desktop.Widgets;

/// <summary>
/// Base for a widget's content. One instance per widget for a signed-in session, shown either as a
/// page in the main window or in an overlay window, so state survives moving between them. Disposed
/// with the session (<see cref="Sessions.SessionHost"/>) when it implements <see cref="IDisposable"/>.
/// </summary>
public abstract partial class WidgetViewModelBase : ViewModelBase
{
    /// <summary>
    /// True while the widget is on screen: its page is selected in a visible main window, or its
    /// overlay is shown. Use it to pause polling and other work while hidden.
    /// </summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }
}
