using Starter.Application.Validation;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Application.Organizations;

/// <summary>The body of <c>POST /api/admin/organizations</c>.</summary>
/// <param name="Code">The unique code (letters, digits, <c>_</c>, <c>-</c>; stored upper-case).</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="Kind">Internal (1) or external (2).</param>
public sealed record CreateOrganizationCommand(string? Code, string? NameEn, string? NameAr, OrganizationKind? Kind);

/// <summary>The body of <c>PUT /api/admin/organizations/{id}</c> (the same fields as a create).</summary>
/// <param name="Code">The unique code.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="Kind">Internal (1) or external (2); it cannot change while accounts belong to the organization.</param>
public sealed record UpdateOrganizationCommand(string? Code, string? NameEn, string? NameAr, OrganizationKind? Kind);

/// <summary>The query of <c>GET /api/admin/organizations</c>; every value is optional.</summary>
/// <param name="Search">Matched against the code and both names.</param>
/// <param name="Kind">Only this kind.</param>
/// <param name="IsActive">Only active or deactivated organizations.</param>
/// <param name="Page">The 1-based page (default 1).</param>
/// <param name="PageSize">The page size (default <see cref="UsersLimits.DefaultPageSize"/>, at most <see cref="UsersLimits.MaxPageSize"/>).</param>
public sealed record OrganizationListQuery(string? Search, OrganizationKind? Kind, bool? IsActive, int? Page, int? PageSize);

/// <summary>Validates <see cref="CreateOrganizationCommand"/>.</summary>
public sealed class CreateOrganizationCommandValidator : IValidator<CreateOrganizationCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(CreateOrganizationCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return OrganizationFieldRules.Check(input.Code, input.NameEn, input.NameAr, input.Kind);
    }
}

/// <summary>Validates <see cref="UpdateOrganizationCommand"/>.</summary>
public sealed class UpdateOrganizationCommandValidator : IValidator<UpdateOrganizationCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(UpdateOrganizationCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return OrganizationFieldRules.Check(input.Code, input.NameEn, input.NameAr, input.Kind);
    }
}

/// <summary>The field rules both organization bodies share (one owner for the create and the edit form).</summary>
public static class OrganizationFieldRules
{
    /// <summary>
    /// The field errors: a well-formed code (<see cref="OrganizationCodes"/>), both names present and within
    /// <see cref="UsersLimits.OrganizationNameMaxLength"/>, a defined kind.
    /// </summary>
    public static ValidationErrors Check(string? code, string? nameEn, string? nameAr, OrganizationKind? kind)
    {
        var errors = new ValidationErrors()
            .RequireText(FieldNames.Of(nameof(CreateOrganizationCommand.Code)), code, UsersLimits.OrganizationCodeMaxLength)
            .RequireText(FieldNames.Of(nameof(CreateOrganizationCommand.NameEn)), nameEn, UsersLimits.OrganizationNameMaxLength)
            .RequireText(FieldNames.Of(nameof(CreateOrganizationCommand.NameAr)), nameAr, UsersLimits.OrganizationNameMaxLength);
        if (!string.IsNullOrWhiteSpace(code) && code.Trim().Length <= UsersLimits.OrganizationCodeMaxLength && !OrganizationCodes.IsWellFormed(code))
        {
            errors.Add(FieldNames.Of(nameof(CreateOrganizationCommand.Code)), ValidationMessages.InvalidFormat);
        }

        return EnumFields.Require(errors, FieldNames.Of(nameof(CreateOrganizationCommand.Kind)), kind);
    }
}
