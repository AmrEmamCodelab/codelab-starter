using Starter.Application.Authorization;
using Starter.Application.Users;
using Starter.Domain.Users;
using Starter.Modules.Users.Application.Accounts;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Application.Import;

/// <summary>A validated import row.</summary>
/// <param name="Row">The row as read.</param>
/// <param name="Kind">The parsed kind, or null when invalid.</param>
/// <param name="Binding">The parsed binding, or null when invalid.</param>
/// <param name="OrganizationId">The organization the code names, or null.</param>
/// <param name="Errors">The row's error codes; empty when the row is valid.</param>
public sealed record CheckedImportRow(ImportRow Row, UserKind? Kind, AccountBinding? Binding, long? OrganizationId, IReadOnlyList<string> Errors)
{
    /// <summary>True when the row can become an account.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Validates import rows (preview and commit run the same checks, so a commit never trusts an old preview): the field
/// rules (<see cref="ImportRowRules"/>), user names and e-mail addresses unique within the file, the organization
/// code (exists, active, matching kind; see <see cref="OrganizationMembership"/>; inside the importer's
/// <see cref="UsersPermissionKeys.Import"/> scope) and user names not already taken.
/// The taken check asks Identity per row; Identity's own create refuses a duplicate too, so a race between preview and
/// commit still ends as a rejected row.
/// </summary>
public sealed class ImportRowValidator(IOrganizationRepository organizations, IAccountAdministration accounts, IOrganizationScope scope)
{
    private const int FirstPage = 1;

    /// <summary>The checked rows, in the order given.</summary>
    public async Task<IReadOnlyList<CheckedImportRow>> ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var byCode = await OrganizationsOfAsync(rows, cancellationToken);
        var decision = await scope.ScopeForAsync(UsersPermissionKeys.Import, cancellationToken);
        var duplicateUserNames = DuplicateKeys(rows.Select(row => row.UserName));
        var duplicateEmails = DuplicateKeys(rows.Select(row => row.Email));
        var checkedRows = new List<CheckedImportRow>(rows.Count);
        foreach (var row in rows)
        {
            var errors = ImportRowRules.FieldErrors(row);
            AddIf(errors, row.UserName is not null && duplicateUserNames.Contains(KeyOf(row.UserName)), UsersImportRowErrorCodes.UserNameDuplicate);
            AddIf(errors, row.Email is not null && duplicateEmails.Contains(KeyOf(row.Email)), UsersImportRowErrorCodes.EmailDuplicate);
            var kind = ImportValues.KindOf(row.Kind);
            var organization = row.OrganizationCode is null ? null : byCode.GetValueOrDefault(OrganizationCodes.Normalize(row.OrganizationCode));
            if (OrganizationError(kind, row, organization, decision) is { } organizationError)
            {
                errors.Add(organizationError);
            }

            if (errors.Count == 0 && await IsTakenAsync(row.UserName!, cancellationToken))
            {
                errors.Add(UsersImportRowErrorCodes.UserNameTaken);
            }

            checkedRows.Add(new CheckedImportRow(row, kind, ImportValues.BindingOf(row.Binding), organization?.Id, errors));
        }

        return checkedRows;
    }

    private static string? OrganizationError(UserKind? kind, ImportRow row, Organization? organization, ScopeDecision decision)
    {
        var membership = kind is { } knownKind
            ? ImportRowRules.ErrorOf(OrganizationMembership.Check(knownKind, organizationRequested: row.OrganizationCode is not null, organization, alreadyMember: false))
            : null;
        return membership ?? (decision.Reaches(organization?.Id) ? null : UsersImportRowErrorCodes.OrganizationOutOfScope);
    }

    private async Task<IReadOnlyDictionary<string, Organization>> OrganizationsOfAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        var codes = rows.Select(row => row.OrganizationCode).OfType<string>().Select(OrganizationCodes.Normalize).Distinct(StringComparer.Ordinal).ToArray();
        return codes.Length == 0
            ? new Dictionary<string, Organization>(StringComparer.Ordinal)
            : await organizations.FindByCodesAsync(codes, cancellationToken);
    }

    private async Task<bool> IsTakenAsync(string userName, CancellationToken cancellationToken)
    {
        var matches = await accounts.ListAsync(
            new AccountQuery(Search: userName, OrganizationId: null, Kind: null, IsActive: null, Page: FirstPage, PageSize: UsersLimits.MaxPageSize),
            cancellationToken);
        return matches.Items.Any(account => string.Equals(account.UserName.Trim(), userName, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> DuplicateKeys(IEnumerable<string?> values) =>
        [.. values.OfType<string>().GroupBy(KeyOf, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key)];

    private static string KeyOf(string value) => value.Trim().ToUpperInvariant();

    private static void AddIf(List<string> errors, bool condition, string code)
    {
        if (condition)
        {
            errors.Add(code);
        }
    }
}
