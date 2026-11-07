namespace Codelab.OpsConsole.Api.Hosting;

/// <summary>
/// <c>Codelab:OpsConsole</c>: who may use the console, where the starter API and Seq are, and the Development-only
/// operator. Validated at start (<see cref="OpsConsoleOptionsValidator"/>). No key material lives here except the Seq
/// API key, which comes only from the environment or the gitignored local secrets file.
/// </summary>
public sealed class OpsConsoleOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Codelab:OpsConsole";

    /// <summary>
    /// The AD group an operator must belong to: a group SID (<c>S-1-5-21-...</c>, preferred: it survives a rename) or a
    /// <c>DOMAIN\Group</c> name. Required.
    /// </summary>
    public string? OperatorGroup { get; set; }

    /// <summary>The service name on every log line and span (ADR-003: a fork renames it in settings, not code).</summary>
    public string? ServiceName { get; set; }

    /// <summary>The starter API the console reads <c>/api/ops</c> from.</summary>
    public OpsApiOptions Api { get; init; } = new();

    /// <summary>The Seq the console reads log events from.</summary>
    public SeqReadOptions Seq { get; init; } = new();

    /// <summary>The Development-only stand-in for Windows sign-in.</summary>
    public DevOperatorOptions DevOperator { get; init; } = new();
}

/// <summary><c>Codelab:OpsConsole:Api</c>: the starter API's base address and the time one call may take.</summary>
public sealed class OpsApiOptions
{
    /// <summary>The default of <see cref="TimeoutSeconds"/>.</summary>
    public const int DefaultTimeoutSeconds = 15;

    /// <summary>The API's base address (<c>https://portal.example/</c>); its <c>/api/ops</c> routes are relative to it. Required.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>How long one call may take before the API counts as unavailable.</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
}

/// <summary><c>Codelab:OpsConsole:Seq</c>: the Seq HTTP API the console reads with a read-only key.</summary>
public sealed class SeqReadOptions
{
    /// <summary>The default of <see cref="TimeoutSeconds"/>.</summary>
    public const int DefaultTimeoutSeconds = 15;

    /// <summary>The default of <see cref="MaxEvents"/>.</summary>
    public const int DefaultMaxEvents = 200;

    /// <summary>The default of <see cref="LookbackDays"/>.</summary>
    public const int DefaultLookbackDays = 31;

    /// <summary>Seq's base address (UI and API share it). Empty: the console runs without log events.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// A Seq API key with the Read permission only. A secret: never in a committed file (the environment variable
    /// <c>Codelab__OpsConsole__Seq__ApiKey</c> or the local secrets file). Empty only for a Seq that needs no key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>How long one Seq query may take before Seq counts as unavailable.</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>The most events one lookup returns.</summary>
    public int MaxEvents { get; set; } = DefaultMaxEvents;

    /// <summary>How far back a lookup searches.</summary>
    public int LookbackDays { get; set; } = DefaultLookbackDays;

    /// <summary>True when <see cref="BaseUrl"/> is set.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>
/// <c>Codelab:OpsConsole:DevOperator</c>: in Development only, signs every request in as <see cref="Name"/> with
/// <see cref="Groups"/>, so the console runs on a machine without a domain. Enabled outside Development refuses to start.
/// </summary>
public sealed class DevOperatorOptions
{
    /// <summary>Use the dev operator instead of Windows sign-in.</summary>
    public bool Enabled { get; set; }

    /// <summary>The account name the dev operator signs in as.</summary>
    public string? Name { get; set; }

    /// <summary>The groups (SIDs or names) the dev operator belongs to; leave out the operator group to see a 403.</summary>
    public IList<string> Groups { get; init; } = [];
}
