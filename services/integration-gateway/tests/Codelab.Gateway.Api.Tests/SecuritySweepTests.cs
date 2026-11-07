namespace Codelab.Gateway.Api.Tests;

/// <summary>The deployable's source passes the repository's security sweep (secrets, private names, unsafe patterns).</summary>
public sealed class SecuritySweepTests
{
    [Fact]
    public void The_gateway_source_is_clean() => Codelab.Testing.Sweeps.DeployableSweep.Check(["services/integration-gateway"]).ThrowIfFailed();
}
