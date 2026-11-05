using Codelab.Security.PublicIds;
using Starter.Application.Authorization;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Application.Validation;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Application.Organizations;

/// <summary>
/// Finds an organization the caller reaches under a permission: an own-organization holder reaches only their own
/// organization, so every other one is not found (one owner of the organization-scope rule for these use cases).
/// </summary>
public sealed class ScopedOrganizations(IOrganizationRepository organizations, IOrganizationScope scope)
{
    /// <summary>The organization when it exists and <paramref name="permissionKey"/> reaches it; otherwise null.</summary>
    public async Task<Organization?> FindAsync(long id, string permissionKey, CancellationToken cancellationToken)
    {
        if (await organizations.FindAsync(id, cancellationToken) is not { } organization)
        {
            return null;
        }

        return (await scope.ScopeForAsync(permissionKey, cancellationToken)).Reaches(organization.Id) ? organization : null;
    }

    /// <summary>
    /// The organization that holds <paramref name="code"/> (normalized here), or null. Not scoped: codes are unique
    /// across every organization.
    /// </summary>
    public Task<Organization?> CodeHolderAsync(string code, CancellationToken cancellationToken) =>
        organizations.FindByCodeAsync(OrganizationCodes.Normalize(code), cancellationToken);

    /// <summary>The caller's decision for <paramref name="permissionKey"/>.</summary>
    public Task<ScopeDecision> DecisionAsync(string permissionKey, CancellationToken cancellationToken) =>
        scope.ScopeForAsync(permissionKey, cancellationToken);
}

/// <summary>
/// <c>GET /api/admin/organizations</c>: one page of organizations, ordered by code. An own-organization
/// <see cref="UsersPermissionKeys.OrganizationsView"/> holder sees only their own organization.
/// </summary>
public sealed class ListOrganizations(IOrganizationRepository organizations, ScopedOrganizations scoped)
{
    private static readonly OrganizationPage Empty = new([], Total: 0);

    /// <summary>The page (never refused: bad paging values are normalized by <see cref="PageRequest"/>).</summary>
    public async Task<Outcome<OrganizationPage>> ExecuteAsync(OrganizationListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = (await scoped.DecisionAsync(UsersPermissionKeys.OrganizationsView, cancellationToken)).Narrow(requestedOrganizationId: null);
        if (filter.MatchesNothing)
        {
            return Empty;
        }

        var page = PageRequest.From(query.Page, query.PageSize);
        return await organizations.ListAsync(
            new OrganizationQuery(PageRequest.Search(query.Search), query.Kind, query.IsActive, page.Page, page.PageSize, Id: filter.OrganizationId),
            cancellationToken);
    }
}

/// <summary><c>GET /api/admin/organizations/{id}</c>.</summary>
public sealed class GetOrganization(ScopedOrganizations scoped)
{
    /// <summary>The organization, or <see cref="UsersErrorCodes.OrganizationNotFound"/> (404, also outside the caller's scope).</summary>
    public async Task<Outcome<Organization>> ExecuteAsync(PublicId<OrganizationIds> id, CancellationToken cancellationToken) =>
        await scoped.FindAsync(id.Value, UsersPermissionKeys.OrganizationsView, cancellationToken) is { } organization
            ? organization
            : Refusal.NotFound(UsersErrorCodes.OrganizationNotFound);
}

/// <summary>
/// <c>POST /api/admin/organizations</c>: a new, active organization with a unique code. Global
/// <see cref="UsersPermissionKeys.OrganizationsManage"/> only: a new organization is never the caller's own.
/// </summary>
public sealed class CreateOrganization(
    IValidator<CreateOrganizationCommand> validator,
    IOrganizationRepository organizations,
    ScopedOrganizations scoped,
    IUsersUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>
    /// The organization, or a refusal (<see cref="PlatformErrorCodes.Validation"/>, <see cref="UsersErrorCodes.OrganizationOutOfScope"/>
    /// (403), <see cref="UsersErrorCodes.OrganizationCodeTaken"/>).
    /// </summary>
    public async Task<Outcome<Organization>> ExecuteAsync(CreateOrganizationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        if ((await scoped.DecisionAsync(UsersPermissionKeys.OrganizationsManage, cancellationToken)).Scope is not PermissionScope.Global)
        {
            return Refusal.Forbidden(UsersErrorCodes.OrganizationOutOfScope);
        }

        var details = new OrganizationDetails(command.Code!, command.NameEn!, command.NameAr!, command.Kind!.Value);
        if (await organizations.FindByCodeAsync(OrganizationCodes.Normalize(details.Code), cancellationToken) is not null)
        {
            return Refusal.Conflict(UsersErrorCodes.OrganizationCodeTaken);
        }

        var organization = Organization.Create(details, timeProvider.GetUtcNow());
        organizations.Add(organization);
        return await unitOfWork.TrySaveChangesAsync(cancellationToken)
            ? organization
            : Refusal.Conflict(UsersErrorCodes.OrganizationCodeTaken);
    }
}

/// <summary>
/// <c>PUT /api/admin/organizations/{id}</c>: replaces the code, the names and the kind. The kind cannot change while
/// accounts belong to the organization (their kind must keep matching it). An own-organization holder reaches only
/// their own organization.
/// </summary>
public sealed class UpdateOrganization(
    IValidator<UpdateOrganizationCommand> validator,
    ScopedOrganizations scoped,
    IAccountAdministration accounts,
    IUsersUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    private const int FirstPage = 1;
    private const int OneItem = 1;

    /// <summary>
    /// The organization, or a refusal (<see cref="PlatformErrorCodes.Validation"/>, <see cref="UsersErrorCodes.OrganizationNotFound"/>,
    /// <see cref="UsersErrorCodes.OrganizationCodeTaken"/>, <see cref="UsersErrorCodes.OrganizationKindInUse"/>).
    /// </summary>
    public async Task<Outcome<Organization>> ExecuteAsync(PublicId<OrganizationIds> id, UpdateOrganizationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        if (await scoped.FindAsync(id.Value, UsersPermissionKeys.OrganizationsManage, cancellationToken) is not { } organization)
        {
            return Refusal.NotFound(UsersErrorCodes.OrganizationNotFound);
        }

        var details = new OrganizationDetails(command.Code!, command.NameEn!, command.NameAr!, command.Kind!.Value);
        if (await RefusalForAsync(organization, details, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        organization.Change(details, timeProvider.GetUtcNow());
        return await unitOfWork.TrySaveChangesAsync(cancellationToken)
            ? organization
            : Refusal.Conflict(UsersErrorCodes.OrganizationCodeTaken);
    }

    private async Task<Refusal?> RefusalForAsync(Organization organization, OrganizationDetails details, CancellationToken cancellationToken)
    {
        var holder = await scoped.CodeHolderAsync(details.Code, cancellationToken);
        if (holder is not null && holder.Id != organization.Id)
        {
            return Refusal.Conflict(UsersErrorCodes.OrganizationCodeTaken);
        }

        if (details.Kind == organization.Kind)
        {
            return null;
        }

        var members = await accounts.ListAsync(
            new AccountQuery(Search: null, OrganizationId: organization.Id, Kind: null, IsActive: null, Page: FirstPage, PageSize: OneItem),
            cancellationToken);
        return members.Total > 0 ? Refusal.Conflict(UsersErrorCodes.OrganizationKindInUse) : null;
    }
}

/// <summary>
/// <c>POST /api/admin/organizations/{id}/deactivate|reactivate</c>. A deactivated organization keeps its accounts but
/// accepts no new member. Repeating the current state is a no-op success. An own-organization holder reaches only their
/// own organization.
/// </summary>
public sealed class SetOrganizationActive(ScopedOrganizations scoped, IUsersUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    /// <summary>Done, or <see cref="UsersErrorCodes.OrganizationNotFound"/> (404, also outside the caller's scope).</summary>
    public async Task<Outcome<Done>> ExecuteAsync(PublicId<OrganizationIds> id, bool isActive, CancellationToken cancellationToken)
    {
        if (await scoped.FindAsync(id.Value, UsersPermissionKeys.OrganizationsManage, cancellationToken) is not { } organization)
        {
            return Refusal.NotFound(UsersErrorCodes.OrganizationNotFound);
        }

        if (organization.SetActive(isActive, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Done.Value;
    }
}
