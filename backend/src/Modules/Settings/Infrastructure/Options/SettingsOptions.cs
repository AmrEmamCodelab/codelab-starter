using Starter.Modules.Settings.Application.Reading;

namespace Starter.Modules.Settings.Infrastructure.Options;

/// <summary>The <c>Settings</c> configuration section: how the runtime settings cache stays fresh.</summary>
public sealed class SettingsOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Settings";

    /// <summary>
    /// True (default): the first settings read of every request scope checks the one-row database stamp, so a change
    /// on any node is seen by the next request. False: no per-request query; changes made on another node are seen
    /// within <c>SettingsLimits.CacheTtl</c> (30 s).
    /// </summary>
    public bool CheckStampPerScope { get; set; } = true;

    /// <summary>The policy the reader applies.</summary>
    public SettingsReadPolicy ToPolicy() => new(CheckStampPerScope);
}
