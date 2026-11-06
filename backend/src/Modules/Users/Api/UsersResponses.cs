using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Application.Results;
using Starter.Domain.Users;
using Starter.Infrastructure.Results;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Accounts;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Api;

/// <summary>An account as the admin screens see it (never a password, hash or ticket).</summary>
/// <param name="Id">The account's PublicId.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Email">The e-mail address, or null.</param>
/// <param name="LoginName">The directory login name, or null.</param>
/// <param name="Upn">The user principal name, or null.</param>
/// <param name="Kind">Internal (1) or external (2).</param>
/// <param name="Binding">Password (1), Active Directory (2) or SSO (3).</param>
/// <param name="OrganizationId">The organization's PublicId, or null.</param>
/// <param name="OrganizationName">The organization's name in the request language, or null.</param>
/// <param name="IsActive">False for a deactivated account.</param>
/// <param name="HasPassword">True once a password-bound account has set its password through the activation link.</param>
/// <param name="IsLockedOut">True while failed sign-ins keep it locked.</param>
/// <param name="IsBreakGlass">True for the configured break-glass administrator.</param>
public sealed record UserResponse(
    [property: WireId] PublicId<UsersIds> Id,
    string UserName,
    string DisplayName,
    string? Email,
    string? LoginName,
    string? Upn,
    UserKind Kind,
    AccountBinding Binding,
    [property: WireId] PublicId<OrganizationIds>? OrganizationId,
    string? OrganizationName,
    bool IsActive,
    bool HasPassword,
    bool IsLockedOut,
    bool IsBreakGlass)
{
    /// <summary>The response of <paramref name="view"/>.</summary>
    public static UserResponse From(UserView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var account = view.Account;
        return new UserResponse(
            Id: PublicId<UsersIds>.From(account.UserId),
            UserName: account.UserName,
            DisplayName: account.DisplayName,
            Email: account.Email,
            LoginName: account.LoginName,
            Upn: account.Upn,
            Kind: account.Kind,
            Binding: account.Binding,
            OrganizationId: account.OrganizationId is { } organizationId ? PublicId<OrganizationIds>.From(organizationId) : null,
            OrganizationName: view.OrganizationName,
            IsActive: account.IsActive,
            HasPassword: account.HasPassword,
            IsLockedOut: account.IsLockedOut,
            IsBreakGlass: account.IsBreakGlass);
    }
}

/// <summary>The body of <c>GET /api/admin/users</c>.</summary>
/// <param name="Items">The accounts of the page.</param>
/// <param name="Total">The number of matches on every page.</param>
public sealed record UserPageResponse(IReadOnlyList<UserResponse> Items, int Total)
{
    /// <summary>The response of <paramref name="page"/>.</summary>
    public static UserPageResponse From(UserViewPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new UserPageResponse([.. page.Items.Select(UserResponse.From)], page.Total);
    }
}

/// <summary>An activation just issued: e-mailed to the user, or a link to hand over.</summary>
/// <param name="ActivationLink">
/// The link (the sealed one-time ticket is in its fragment) when <paramref name="Delivery"/> is AdministratorLink (1);
/// null when it is Email (2), because the user receives the link by e-mail.
/// </param>
/// <param name="ExpiresAt">When it stops working (an e-mailed link expires a little later, by the outbox delay).</param>
/// <param name="Delivery">AdministratorLink (1) or Email (2).</param>
public sealed record ActivationResponse(string? ActivationLink, DateTimeOffset ExpiresAt, ActivationDelivery Delivery)
{
    /// <summary>The response of <paramref name="activation"/>.</summary>
    public static ActivationResponse From(IssuedActivation activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        return new ActivationResponse(activation.ActivationLink, activation.ExpiresAt, activation.Delivery);
    }
}

/// <summary>The body of a successful <c>POST /api/admin/users</c> (201).</summary>
/// <param name="User">The account.</param>
/// <param name="Activation">Its activation (password binding only), or null.</param>
public sealed record CreatedUserResponse(UserResponse User, ActivationResponse? Activation)
{
    /// <summary>The response of <paramref name="created"/>.</summary>
    public static CreatedUserResponse From(CreatedUser created)
    {
        ArgumentNullException.ThrowIfNull(created);
        return new CreatedUserResponse(
            UserResponse.From(created.User),
            created.Activation is null ? null : ActivationResponse.From(created.Activation));
    }
}

/// <summary>An organization.</summary>
/// <param name="Id">The organization's PublicId.</param>
/// <param name="Code">The unique code (upper-case).</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="Kind">Internal (1) or external (2).</param>
/// <param name="IsActive">False once deactivated.</param>
public sealed record OrganizationResponse(
    [property: WireId] PublicId<OrganizationIds> Id,
    string Code,
    string NameEn,
    string NameAr,
    OrganizationKind Kind,
    bool IsActive)
{
    /// <summary>The response of <paramref name="organization"/>.</summary>
    public static OrganizationResponse From(Organization organization)
    {
        ArgumentNullException.ThrowIfNull(organization);
        return new OrganizationResponse(
            PublicId<OrganizationIds>.From(organization.Id),
            organization.Code,
            organization.NameEn,
            organization.NameAr,
            organization.Kind,
            organization.IsActive);
    }
}

/// <summary>The body of <c>GET /api/admin/organizations</c>.</summary>
/// <param name="Items">The organizations of the page.</param>
/// <param name="Total">The number of matches on every page.</param>
public sealed record OrganizationPageResponse(IReadOnlyList<OrganizationResponse> Items, int Total)
{
    /// <summary>The response of <paramref name="page"/>.</summary>
    public static OrganizationPageResponse From(OrganizationPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new OrganizationPageResponse([.. page.Items.Select(OrganizationResponse.From)], page.Total);
    }
}

/// <summary>Maps outcomes to 201 Created (the kernel <see cref="OutcomeResults"/> maps 200 and 204).</summary>
public static class CreatedResults
{
    /// <summary>201 with <paramref name="map"/> of the value, or the refusal's problem+json.</summary>
    public static ActionResult<TResponse> ToCreatedResult<T, TResponse>(this Outcome<T> outcome, Func<T, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(map);
        return outcome.IsSuccess
            ? new ObjectResult(map(outcome.Value)) { StatusCode = StatusCodes.Status201Created }
            : outcome.Refusal.ToActionResult();
    }
}
