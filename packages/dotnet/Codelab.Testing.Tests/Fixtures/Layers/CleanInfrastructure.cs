using Codelab.Testing.Tests.Fixtures.Layers.Clean.Application;
using Codelab.Testing.Tests.Fixtures.Layers.Clean.Domain;
using Microsoft.EntityFrameworkCore;

namespace Codelab.Testing.Tests.Fixtures.Layers.Clean.Infrastructure;

/// <summary>Clean module: the DbContext lives in Infrastructure.</summary>
public sealed class CleanDbContext(DbContextOptions<CleanDbContext> options) : DbContext(options);

/// <summary>Clean module: the port's adapter.</summary>
public sealed class CleanThingStore(CleanDbContext context) : ICleanThingStore
{
    public CleanDbContext Context { get; } = context;

    public CleanThing? Find(string name) => null;
}
