using Codelab.Testing.Tests.Fixtures.Layers.Dirty.Infrastructure;

namespace Codelab.Testing.Tests.Fixtures.Layers.Dirty.Api;

/// <summary>Dirty module: an endpoint using the DbContext directly (violation).</summary>
public sealed class DirtyEndpoint(DirtyDbContext context)
{
    public DirtyDbContext Context { get; } = context;
}
