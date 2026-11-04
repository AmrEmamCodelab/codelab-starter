namespace Codelab.Testing.Tests.Fixtures.Modules.Alpha;

/// <summary>Module Alpha: may use Beta, must not use Gamma.</summary>
public sealed class AlphaService(Codelab.Testing.Tests.Fixtures.Modules.Beta.BetaThing beta)
{
    public Codelab.Testing.Tests.Fixtures.Modules.Beta.BetaThing Beta { get; } = beta;
}
