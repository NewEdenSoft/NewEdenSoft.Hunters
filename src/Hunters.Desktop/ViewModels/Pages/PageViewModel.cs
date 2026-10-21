using CommunityToolkit.Mvvm.ComponentModel;

namespace NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

/// <summary>A page in the main window's navigation rail.</summary>
public abstract partial class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }

    /// <summary>Short status chip shown next to the title in the navigation rail.</summary>
    public virtual string? Badge => null;

    /// <summary>The page fills the height of the content area instead of scrolling (a docked map).</summary>
    public virtual bool IsFullBleed => false;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
