using Microsoft.EntityFrameworkCore;
using Starter.Infrastructure.Persistence;

namespace Starter.Infrastructure.Numbering;

/// <summary>
/// The <c>numbering</c> schema: no tables, only the per-prefix-and-year sequences <see cref="SqlNumberSequences"/>
/// creates on demand. Its migration creates the schema (and its own migrations history), so the schema reaches
/// production through the DBA scripts like every other.
/// </summary>
public sealed class NumberingDbContext(DbContextOptions<NumberingDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The database schema of the sequences.</summary>
    public const string SchemaName = "numbering";

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}
