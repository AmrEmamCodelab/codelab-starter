using Microsoft.EntityFrameworkCore;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Infrastructure.Persistence;

/// <summary>The organizations on <see cref="UsersDbContext"/>.</summary>
public sealed class OrganizationRepository(UsersDbContext context) : IOrganizationRepository
{
    /// <inheritdoc />
    public Task<Organization?> FindAsync(long id, CancellationToken cancellationToken) =>
        context.Organizations.SingleOrDefaultAsync(organization => organization.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Organization?> FindByCodeAsync(string normalizedCode, CancellationToken cancellationToken) =>
        context.Organizations.SingleOrDefaultAsync(organization => organization.Code == normalizedCode, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, Organization>> FindByCodesAsync(IReadOnlyCollection<string> normalizedCodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(normalizedCodes);
        var found = await context.Organizations.Where(organization => normalizedCodes.Contains(organization.Code)).ToListAsync(cancellationToken);
        return found.ToDictionary(organization => organization.Code, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, Organization>> FindManyAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var found = await context.Organizations.AsNoTracking().Where(organization => ids.Contains(organization.Id)).ToListAsync(cancellationToken);
        return found.ToDictionary(organization => organization.Id);
    }

    /// <inheritdoc />
    public async Task<OrganizationPage> ListAsync(OrganizationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var matches = Filter(context.Organizations.AsNoTracking(), query);
        var total = await matches.CountAsync(cancellationToken);
        var items = await matches
            .OrderBy(organization => organization.Code)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new OrganizationPage(items, total);
    }

    /// <inheritdoc />
    public void Add(Organization organization) => context.Organizations.Add(organization);

    private static IQueryable<Organization> Filter(IQueryable<Organization> organizations, OrganizationQuery query)
    {
        if (query.Id is { } id)
        {
            organizations = organizations.Where(organization => organization.Id == id);
        }

        if (query.Search is { } search)
        {
            organizations = organizations.Where(organization =>
                organization.Code.Contains(search) || organization.NameEn.Contains(search) || organization.NameAr.Contains(search));
        }

        if (query.Kind is { } kind)
        {
            organizations = organizations.Where(organization => organization.Kind == kind);
        }

        if (query.IsActive is { } isActive)
        {
            organizations = organizations.Where(organization => organization.IsActive == isActive);
        }

        return organizations;
    }
}

/// <summary>The import batches on <see cref="UsersDbContext"/>.</summary>
public sealed class ImportBatchRepository(UsersDbContext context) : IImportBatchRepository
{
    /// <inheritdoc />
    public Task<ImportBatch?> FindAsync(long id, CancellationToken cancellationToken) =>
        context.ImportBatches.SingleOrDefaultAsync(batch => batch.Id == id, cancellationToken);

    /// <inheritdoc />
    public void Add(ImportBatch batch) => context.ImportBatches.Add(batch);
}
