using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

/// <summary>
/// How Hunters works, like the web's Info page (<c>routes/_authed/info.tsx</c>): using the map, what its
/// marks and the widgets' words mean, how the profiles are scored, and each hunting profile with its
/// description and evidence from <c>/api/hunting/profiles</c>, asked for when the page is opened, once
/// per session.
/// </summary>
public sealed partial class InfoPageViewModel : PageViewModel
{
    private readonly AccountPageViewModel _account;
    private readonly ThemeCatalog _catalog;

    /// <summary>The session the profiles were asked for (or are on their way for); null before.</summary>
    private HuntersSession? _askedFor;

    public InfoPageViewModel(AccountPageViewModel account, ThemeCatalog catalog)
    {
        _account = account;
        _catalog = catalog;
        account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPageViewModel.Session))
                Load();
        };
    }

    public override string Title => "Info";

    /// <summary>The badge drawn for the map's Badges entry: the Carriers profile's.</summary>
    public ProfileStyle BadgeExample => _catalog.ProfileStyle("carriers", "Carriers");

    /// <summary>The server's hunting profiles, as it lists them; empty until they come.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfiles))]
    public partial IReadOnlyList<InfoProfile> Profiles { get; private set; } = [];

    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>"Loading…", or why there are none, in place of the profiles; null with profiles.</summary>
    [ObservableProperty]
    public partial string? ProfilesText { get; private set; } = "Log in to see the hunting profiles.";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsSelected) && IsSelected)
            Load();
    }

    /// <summary>Asks for the profiles while the page is shown, once per session.</summary>
    private void Load()
    {
        var session = _account.Session is { IsEnded: false } s ? s : null;
        if (session is null)
        {
            _askedFor = null;
            Profiles = [];
            ProfilesText = "Log in to see the hunting profiles.";
            return;
        }

        if (!IsSelected || session == _askedFor)
            return;

        _askedFor = session;
        _ = LoadAsync(session);
    }

    private async Task LoadAsync(HuntersSession session)
    {
        Profiles = [];
        ProfilesText = "Loading…";
        try
        {
            var profiles = await session.GetHuntingProfilesAsync();
            if (session != _askedFor)
                return;
            Profiles = [.. profiles.Select(p => InfoProfile.Create(p, _catalog.ProfileStyle(p.Id, p.Name)))];
            ProfilesText = Profiles.Count == 0 ? "The server has no hunting profiles yet." : null;
        }
        catch (HuntersApiException)
        {
            if (session != _askedFor)
                return;
            // Asked again the next time the page is opened.
            _askedFor = null;
            ProfilesText = "The server has no hunting profiles yet.";
        }
    }
}

/// <summary>A hunting profile on the Info page: its badge and name, what it is, and what counts as seeing it.</summary>
public sealed record InfoProfile(ProfileStyle Style, string Name, string Description, IReadOnlyList<string> Evidence)
{
    public bool HasDescription => Description.Length > 0;

    public bool HasEvidence => Evidence.Count > 0;

    public static InfoProfile Create(HuntingProfileInfo profile, ProfileStyle style) =>
        new(style, profile.Name, profile.Description, profile.Evidence ?? []);
}
