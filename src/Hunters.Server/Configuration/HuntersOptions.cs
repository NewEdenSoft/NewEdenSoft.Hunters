using System.ComponentModel.DataAnnotations;

namespace NewEdenSoft.Hunters.Server.Configuration;

/// <summary>Root of the <c>Hunters</c> configuration section.</summary>
public sealed class HuntersOptions
{
    public const string SectionName = "Hunters";

    /// <summary>Display name shown to clients in the discovery document.</summary>
    [Required]
    public string Name { get; set; } = "Hunters";

    public WebOptions Web { get; set; } = new();

    public DataProtectionSettings DataProtection { get; set; } = new();

    public DatabaseOptions Database { get; set; } = new();

    public SsoOptions Sso { get; set; } = new();

    public AdmissionOptions Admission { get; set; } = new();

    /// <summary>EVE character ids whose accounts are server admins. Blank entries are ignored.</summary>
    public string[] Admins { get; set; } = [];

    public long[] AdminCharacterIds =>
        [.. Admins.Select(a => long.TryParse(a, out var id) ? id : 0).Where(id => id > 0)];
}

public sealed class WebOptions
{
    /// <summary>Serve the bundled web app from <c>wwwroot</c>.</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class DataProtectionSettings
{
    /// <summary>
    /// Directory where Data Protection keys are persisted. Must survive restarts, otherwise stored
    /// refresh tokens become unreadable. Defaults to a folder under local application data.
    /// </summary>
    public string? KeysPath { get; set; }
}

public sealed class DatabaseOptions
{
    /// <summary>Apply pending EF Core migrations when the server starts.</summary>
    public bool MigrateOnStartup { get; set; } = true;
}

/// <summary>The server's EVE SSO application (developers.eveonline.com).</summary>
public sealed class SsoOptions
{
    public string ClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    /// <summary>Must match the application's single callback URL, e.g. <c>https://host/auth/eve/callback</c>.</summary>
    public string CallbackUrl { get; set; } = "";

    public string[] Scopes { get; set; } =
    [
        "esi-location.read_location.v1",
        "esi-location.read_online.v1",
        "esi-location.read_ship_type.v1",
        "esi-ui.write_waypoint.v1",
    ];

    public string AuthorizeUrl { get; set; } = "https://login.eveonline.com/v2/oauth/authorize";
    public string TokenUrl { get; set; } = "https://login.eveonline.com/v2/oauth/token";
    public string RevokeUrl { get; set; } = "https://login.eveonline.com/v2/oauth/revoke";
    public string MetadataUrl { get; set; } = "https://login.eveonline.com/.well-known/oauth-authorization-server";

    /// <summary>Accepted <c>iss</c> values of EVE access tokens.</summary>
    public string[] Issuers { get; set; } = ["https://login.eveonline.com", "login.eveonline.com"];

    public bool IsConfigured => ClientId.Length > 0 && ClientSecret.Length > 0 && CallbackUrl.Length > 0;
}

public sealed class AdmissionOptions
{
    /// <summary><see cref="AdmissionMode.All"/>: every enabled rule must pass. <see cref="AdmissionMode.Any"/>: one is enough.</summary>
    public AdmissionMode Mode { get; set; } = AdmissionMode.All;

    public AccessKeyOptions AccessKey { get; set; } = new();

    public AffiliationOptions Affiliation { get; set; } = new();

    public CharacterListOptions Characters { get; set; } = new();

    /// <summary>How often existing accounts are checked against the rules again.</summary>
    public TimeSpan RecheckInterval { get; set; } = TimeSpan.FromHours(1);
}

public enum AdmissionMode
{
    All,
    Any,
}

public sealed class AccessKeyOptions
{
    /// <summary>Creating a new account requires a valid access key.</summary>
    public bool Enabled { get; set; }
}

public sealed class AffiliationOptions
{
    public bool Enabled { get; set; }

    public long[] CorporationIds { get; set; } = [];

    public long[] AllianceIds { get; set; } = [];

    public AffiliationScope Scope { get; set; } = AffiliationScope.FirstCharacter;
}

public enum AffiliationScope
{
    /// <summary>Only the account's main character must qualify.</summary>
    FirstCharacter,

    /// <summary>Every character must qualify.</summary>
    AllCharacters,
}

public sealed class CharacterListOptions
{
    /// <summary>When non-empty, an admission rule: the character must be listed.</summary>
    public long[] Allow { get; set; } = [];

    /// <summary>Always refused, whatever the other rules say.</summary>
    public long[] Deny { get; set; } = [];
}
