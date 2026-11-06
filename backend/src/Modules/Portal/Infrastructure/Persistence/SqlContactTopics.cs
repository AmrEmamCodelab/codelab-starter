using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Starter.Domain.Lookups;
using Starter.Modules.Portal.Application;

namespace Starter.Modules.Portal.Infrastructure.Persistence;

/// <summary>
/// The <see cref="IContactTopics"/> over the <c>contact-topics</c> lookup values: one read-only, parameterized query on
/// the Lookups module's value table (the kernel has no lookup-read port yet; see MODULE.md, Gotchas). Read through this
/// module's connection, so the Portal project never references the Lookups project.
/// </summary>
public sealed class SqlContactTopics(PortalDbContext context) : IContactTopics
{
    /// <summary>The schema of the lookup tables (owned by the Lookups module).</summary>
    public const string LookupsSchema = "lookups";

    /// <summary>The lookup value table (owned by the Lookups module).</summary>
    public const string LookupItemsTable = "LookupItems";

    private const string TypeParameter = "@typeKey";
    private const string IdParameter = "@id";

    /// <summary>The query: the active value of the contact-topics type with the given id.</summary>
    public const string Query =
        "SELECT [Id], [NameAr], [NameEn] FROM [" + LookupsSchema + "].[" + LookupItemsTable + "] "
        + "WHERE [TypeKey] = " + TypeParameter + " AND [Id] = " + IdParameter + " AND [IsActive] = CAST(1 AS bit)";

    /// <inheritdoc />
    public async Task<ContactTopic?> FindActiveAsync(int topicId, CancellationToken cancellationToken)
    {
        var rows = await context.Database
            .SqlQueryRaw<TopicRow>(Query, new SqlParameter(TypeParameter, LookupTypes.ContactTopics), new SqlParameter(IdParameter, topicId))
            .ToListAsync(cancellationToken);
        return rows is [var row] ? new ContactTopic(row.Id, row.NameAr, row.NameEn) : null;
    }

    /// <summary>One row of <see cref="Query"/> (settable properties: EF materializes it by column name).</summary>
    public sealed class TopicRow
    {
        /// <summary>The lookup id.</summary>
        public int Id { get; init; }

        /// <summary>The Arabic name.</summary>
        public string NameAr { get; init; } = string.Empty;

        /// <summary>The English name.</summary>
        public string NameEn { get; init; } = string.Empty;
    }
}
