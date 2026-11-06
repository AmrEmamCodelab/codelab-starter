namespace Starter.Modules.Jobs.Application;

/// <summary>
/// The permission keys the jobs module declares (format: <c>Starter.Application.Authorization.PermissionKeyFormat</c>).
/// Guard endpoints with these constants, never with literals.
/// </summary>
public static class JobsPermissionKeys
{
    /// <summary>List the background jobs and their run history.</summary>
    public const string View = "jobs.view";

    /// <summary>Run a background job now.</summary>
    public const string Run = "jobs.run";

    // scaffold:permission-keys
}
