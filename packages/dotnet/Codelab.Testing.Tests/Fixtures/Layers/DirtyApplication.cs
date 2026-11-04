using Codelab.Testing.Tests.Fixtures.Layers.Dirty.Infrastructure;

namespace Codelab.Testing.Tests.Fixtures.Layers.Dirty.Application;

/// <summary>Dirty module: a use case holding the DbContext (violation).</summary>
public sealed class DirtyService(DirtyDbContext context)
{
    public DirtyDbContext Context { get; } = context;
}
