using Starter.Application.Persistence;
using Starter.Modules.Users.Application.Import;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Application;

/// <summary>The Users module's persistence boundary (implemented by its EF Core context in Infrastructure).</summary>
public interface IUsersUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Writes every staged change in one transaction; false (and nothing written) when a unique index or a concurrency
    /// token refused the write, which means a concurrent request won the race (a duplicate code, a second commit).
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>The organizations.</summary>
public interface IOrganizationRepository
{
    /// <summary>The organization, or null.</summary>
    Task<Organization?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>The organization with this normalized code (<see cref="OrganizationCodes.Normalize"/>), or null.</summary>
    Task<Organization?> FindByCodeAsync(string normalizedCode, CancellationToken cancellationToken);

    /// <summary>The organizations with these normalized codes, keyed by code; unknown codes are absent.</summary>
    Task<IReadOnlyDictionary<string, Organization>> FindByCodesAsync(IReadOnlyCollection<string> normalizedCodes, CancellationToken cancellationToken);

    /// <summary>The organizations with these ids, keyed by id; unknown ids are absent.</summary>
    Task<IReadOnlyDictionary<long, Organization>> FindManyAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken);

    /// <summary>One page of organizations matching the query, ordered by code.</summary>
    Task<OrganizationPage> ListAsync(OrganizationQuery query, CancellationToken cancellationToken);

    /// <summary>Stages a new organization (written by the unit of work).</summary>
    void Add(Organization organization);
}

/// <summary>A page of the organization list. Every filter is optional (null matches all).</summary>
/// <param name="Search">Matched against the code and both names, or null.</param>
/// <param name="Kind">Only this kind, or null.</param>
/// <param name="IsActive">Only active (true) or deactivated (false) organizations, or null.</param>
/// <param name="Page">The 1-based page number.</param>
/// <param name="PageSize">The page size (at most <see cref="UsersLimits.MaxPageSize"/>).</param>
/// <param name="Id">Only the organization with this id (an own-organization scope), or null.</param>
public sealed record OrganizationQuery(string? Search, OrganizationKind? Kind, bool? IsActive, int Page, int PageSize, long? Id = null);

/// <summary>One page of organizations and the total number of matches.</summary>
/// <param name="Items">The organizations of the page.</param>
/// <param name="Total">The number of matches on every page.</param>
public sealed record OrganizationPage(IReadOnlyList<Organization> Items, int Total);

/// <summary>The user import batches.</summary>
public interface IImportBatchRepository
{
    /// <summary>The batch, or null.</summary>
    Task<ImportBatch?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>Stages a new batch (written by the unit of work).</summary>
    void Add(ImportBatch batch);
}

/// <summary>The PublicId token of a user id, for payloads that leave the request (the outbox).</summary>
public interface IUserPublicIds
{
    /// <summary>The <c>users</c> PublicId token of <paramref name="userId"/>.</summary>
    string TokenOf(long userId);
}

/// <summary>Reads the first worksheet of an .xlsx workbook into text cells, within the import caps.</summary>
public interface IUserSheetReader
{
    /// <summary>
    /// The header row and the non-blank data rows of the first worksheet, or the reason it could not be read. Never
    /// throws for a bad file: every unreadable, oversized or malformed workbook is a <see cref="SheetReadStatus"/>.
    /// </summary>
    /// <param name="content">The uploaded bytes (read once, not disposed).</param>
    /// <param name="maxDataRows">The most data rows accepted; one more is <see cref="SheetReadStatus.TooManyRows"/>.</param>
    SheetReadResult Read(Stream content, int maxDataRows);
}
