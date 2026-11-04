namespace Codelab.Testing.Tests.Fixtures.Layers.Dirty.Domain;

/// <summary>Dirty module: a domain type that reaches out into Application (violation).</summary>
public sealed class DirtyThing
{
    public Codelab.Testing.Tests.Fixtures.Layers.Dirty.Application.DirtyService? Service { get; set; }
}
