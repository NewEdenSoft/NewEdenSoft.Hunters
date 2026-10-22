namespace NewEdenSoft.Hunters.Intel;

/// <summary>Root of the intel service's <c>Hunters</c> configuration section (the same root as the API server's).</summary>
public sealed class IntelOptions
{
    public const string SectionName = "Hunters";

    public IntelDatabaseOptions Database { get; set; } = new();

    public ActivityOptions Activity { get; set; } = new();

    public EsiOptions Esi { get; set; } = new();

    public IntelSdeOptions Sde { get; set; } = new();
}

/// <summary>The SDE catalogue (systems, regions, types). Its cache path is <c>Hunters:Sde:CachePath</c>, as for the server.</summary>
public sealed class IntelSdeOptions
{
    /// <summary>How often to check CCP for a new SDE build.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(6);
}

public sealed class IntelDatabaseOptions
{
    /// <summary>Apply pending migrations of the <c>intel</c> schema when the service starts.</summary>
    public bool MigrateOnStartup { get; set; } = true;
}

/// <summary>ESI's hourly kills and jumps per system.</summary>
public sealed class ActivityOptions
{
    /// <summary>Collect ESI's hourly kills and jumps per system.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long raw hourly snapshots are kept. 90 days gives about 13 samples per hour of the week.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>An hour counts as a system's peak when its average is at least this share of the busiest hour's.</summary>
    public double PeakThreshold { get; set; } = 0.8;

    /// <summary>Peaks are only given once each hour of the week has this many samples (weeks of data).</summary>
    public int PeakMinWeeks { get; set; } = 3;

    /// <summary>Peaks are only given when the busiest hour averages at least this much, so near-silent systems get none.</summary>
    public double PeakMinAverage { get; set; } = 1;
}

public sealed class EsiOptions
{
    /// <summary>How CCP can reach the operator, sent in the User-Agent of every ESI call.</summary>
    public string Contact { get; set; } = "https://github.com/NewEdenSoft/NewEdenSoft.Hunters";
}
