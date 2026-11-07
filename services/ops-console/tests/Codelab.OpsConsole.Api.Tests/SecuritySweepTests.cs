namespace Codelab.OpsConsole.Api.Tests;

/// <summary>The deployable's source passes the repository's security sweep (secrets, private names, unsafe patterns).</summary>
public sealed class SecuritySweepTests
{
    [Fact]
    public void The_ops_console_source_is_clean() => Codelab.Testing.Sweeps.DeployableSweep.Check(["services/ops-console"]).ThrowIfFailed();
}
