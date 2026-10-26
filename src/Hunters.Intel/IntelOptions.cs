namespace NewEdenSoft.Hunters.Intel;

/// <summary>Root of the intel service's <c>Hunters</c> configuration section (the same root as the API server's).</summary>
public sealed class IntelOptions
{
    public const string SectionName = "Hunters";

    public IntelDatabaseOptions Database { get; set; } = new();

    public ActivityOptions Activity { get; set; } = new();

    public KillsOptions Kills { get; set; } = new();

    public HuntingOptions Hunting { get; set; } = new();

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

/// <summary>zKillboard's R2Z2 kill feed.</summary>
public sealed class KillsOptions
{
    /// <summary>Read the feed and store kills.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long kills are kept.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(90);

    public Uri FeedUrl { get; set; } = new("https://r2z2.zkillboard.com/ephemeral/");

    /// <summary>
    /// The most requests a second to R2Z2, for everything the service asks it (<see cref="Kills.KillFeedGate"/>).
    /// zKillboard blocks an address for an hour above 15; 5 leaves room for a second reader on the same
    /// address, and catches up a day of kills in about two hours.
    /// </summary>
    public double MaxRequestsPerSecond { get; set; } = 5;

    /// <summary>The pause after zKillboard blocks the address (429 or 403) without saying for how long.</summary>
    public TimeSpan BlockedPause { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The wait when the next file isn't written yet; zKillboard asks for at least 6 seconds.</summary>
    public TimeSpan WaitWhenCaughtUp { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>zKillboard keeps files for about a day; a saved position older than this is skipped to the newest.</summary>
    public TimeSpan ResumeWithin { get; set; } = TimeSpan.FromHours(20);

    public KillRefetchOptions Refetch { get; set; } = new();
}

/// <summary>Fetching stored kills again from ESI for what they didn't keep (<see cref="Kills.KillRefetchService"/>).</summary>
public sealed class KillRefetchOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>ESI calls a second: slow, so a backlog drains over hours and leaves ESI's limits alone.</summary>
    public double PerSecond { get; set; } = 1;

    /// <summary>Kills picked at once (newest first) between progress logs.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>How often to look for kills to fetch once none are left.</summary>
    public TimeSpan IdleInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>Hunting profiles: which sightings are kept out of the counts as fleet fights.</summary>
public sealed class HuntingOptions
{
    /// <summary>A kill with more attackers than this is a fleet fight.</summary>
    public int FleetAttackers { get; set; } = 25;

    /// <summary>This many player kills in a system within <see cref="BattleWindow"/> either side of a sighting is a battle.</summary>
    public int BattleKills { get; set; } = 20;

    public TimeSpan BattleWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>A sighting's weight in the score halves every this long.</summary>
    public TimeSpan ScoreHalfLife { get; set; } = TimeSpan.FromDays(14);

    /// <summary>The aggregates are recomputed at most this often while sightings arrive (and at least hourly).</summary>
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>A regular's time of day: the hours of a typical play session.</summary>
    public int RegularWindowHours { get; set; } = 4;

    /// <summary>The wider time of day a regular may fall in when the session window doesn't hold.</summary>
    public int RegularExtendedWindowHours { get; set; } = 6;

    /// <summary>Different days inside the window that make a pilot a regular.</summary>
    public int RegularMinDays { get; set; } = 2;

    /// <summary>A regular only the extended window holds counts this much: weaker evidence than a session's.</summary>
    public double RegularExtendedWeight { get; set; } = 0.5;
}

public sealed class EsiOptions
{
    /// <summary>How CCP can reach the operator, sent in the User-Agent of every ESI call.</summary>
    public string Contact { get; set; } = "https://github.com/NewEdenSoft/NewEdenSoft.Hunters";
}
