using Codelab.Testing.Tests.Fixtures.Layers.Clean.Domain;

namespace Codelab.Testing.Tests.Fixtures.Layers.Clean.Application;

/// <summary>Clean module: a port declared in Application.</summary>
public interface ICleanThingStore
{
    CleanThing? Find(string name);
}

/// <summary>Clean module: a use case that depends on the port only.</summary>
public sealed class CleanThingService(ICleanThingStore store)
{
    public CleanThing? Get(string name) => store.Find(name);
}
