using Codelab.Testing.Tests.Fixtures.Layers.Clean.Infrastructure;

namespace Codelab.Testing.Tests.Fixtures.Layers.Clean.Api.Composition;

/// <summary>Clean module: the composition root may name the DbContext.</summary>
public static class CleanRegistration
{
    public static Type ContextType => typeof(CleanDbContext);
}
