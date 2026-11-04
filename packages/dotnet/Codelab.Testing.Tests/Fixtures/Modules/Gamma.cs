namespace Codelab.Testing.Tests.Fixtures.Modules.Gamma;

/// <summary>Module Gamma: may use Alpha.</summary>
public sealed class GammaThing(Codelab.Testing.Tests.Fixtures.Modules.Alpha.AlphaService alpha)
{
    public Codelab.Testing.Tests.Fixtures.Modules.Alpha.AlphaService Alpha { get; } = alpha;
}
