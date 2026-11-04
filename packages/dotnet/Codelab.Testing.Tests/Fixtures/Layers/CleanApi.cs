using Codelab.Testing.Tests.Fixtures.Layers.Clean.Application;

namespace Codelab.Testing.Tests.Fixtures.Layers.Clean.Api;

/// <summary>Clean module: a thin endpoint calling one use case.</summary>
public sealed class CleanThingEndpoint(CleanThingService service)
{
    public string Describe(string name) => service.Get(name)?.Name ?? string.Empty;
}
