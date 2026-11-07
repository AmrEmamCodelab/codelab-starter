namespace Starter.Architecture.Tests;

/// <summary>The backend and the web app sources pass the repository's security sweep (secrets, private names, unsafe patterns).</summary>
public sealed class SecuritySweepTests
{
    [Fact]
    public void The_backend_source_is_clean() => Codelab.Testing.Sweeps.DeployableSweep.Check(["backend"]).ThrowIfFailed();

    [Fact]
    public void The_web_app_source_is_clean() => Codelab.Testing.Sweeps.DeployableSweep.Check(["frontend/starter-web"]).ThrowIfFailed();
}
