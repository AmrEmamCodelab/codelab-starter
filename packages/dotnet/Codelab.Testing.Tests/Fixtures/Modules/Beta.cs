namespace Codelab.Testing.Tests.Fixtures.Modules.Beta;

/// <summary>Module Beta: uses nothing but AlphaExtra (which it may not), never Alpha.</summary>
public sealed class BetaThing
{
    public Codelab.Testing.Tests.Fixtures.Modules.AlphaExtra.AlphaExtraThing? Extra { get; set; }
}
