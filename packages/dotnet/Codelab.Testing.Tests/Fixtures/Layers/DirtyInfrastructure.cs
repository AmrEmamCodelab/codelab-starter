using Microsoft.EntityFrameworkCore;

namespace Codelab.Testing.Tests.Fixtures.Layers.Dirty.Infrastructure;

/// <summary>Dirty module: its DbContext.</summary>
public sealed class DirtyDbContext(DbContextOptions<DirtyDbContext> options) : DbContext(options);
