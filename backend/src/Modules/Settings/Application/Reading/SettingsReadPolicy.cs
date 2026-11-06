namespace Starter.Modules.Settings.Application.Reading;

/// <summary>How the runtime reader keeps its snapshot fresh (from <c>Settings:*</c> options).</summary>
/// <param name="CheckStampPerScope">
/// True (default): the first read in each scope compares the database stamp with the snapshot's version, so a change on
/// any node is seen by the next request. False: no per-scope query; only the <c>SettingsLimits.CacheTtl</c> bound applies.
/// </param>
public sealed record SettingsReadPolicy(bool CheckStampPerScope);
