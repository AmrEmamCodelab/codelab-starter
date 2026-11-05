namespace Starter.Infrastructure.Startup;

/// <summary>When a startup task runs, relative to the others. Tasks of a lower phase finish before higher ones start.</summary>
public enum StartupPhase
{
    /// <summary>Code-owned reference data written to the database (the permission catalog).</summary>
    ReferenceData = 1,

    /// <summary>Accounts seeded from configuration (the break-glass administrator, Development users).</summary>
    Accounts = 2,
}

/// <summary>
/// Work a module runs once when the host starts, after migrations (when enabled) and in <see cref="Phase"/> order. A
/// task must be idempotent: it runs on every start of every node. A failure stops the host.
/// </summary>
public interface IStartupTask
{
    /// <summary>The phase the task belongs to.</summary>
    StartupPhase Phase { get; }

    /// <summary>Runs the task inside its own DI scope.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
