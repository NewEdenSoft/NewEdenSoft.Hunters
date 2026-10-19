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

    public AdmissionOptions Admission { get; set; } = new();
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

public sealed class AdmissionOptions
{
    public AccessKeyOptions AccessKey { get; set; } = new();
}

public sealed class AccessKeyOptions
{
    public bool Enabled { get; set; }
}
