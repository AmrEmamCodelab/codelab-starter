using Starter.Application.Users;
using Starter.Application.Validation;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.Accounts;

/// <summary>
/// Validates the account administration inputs (<see cref="NewAccount"/>, <see cref="AccountChanges"/>): required and
/// bounded names, well-shaped optional identifiers, defined enum values and a positive organization id. Every error
/// of the input is returned at once. E-mail, login name and UPN must be plain ASCII: they are matched against external
/// identities, and Unicode case folding could map a look-alike onto another person's account.
/// </summary>
public sealed class AccountInputValidator : IValidator<NewAccount>, IValidator<AccountChanges>
{
    private const char AtSign = '@';
    private const char Dot = '.';

    /// <inheritdoc />
    public ValidationErrors Validate(NewAccount input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors()
            .RequireText(FieldNames.Of(nameof(NewAccount.UserName)), input.UserName, IdentityLimits.UserNameMaxLength);
        ValidateProfile(errors, AccountSummaries.ChangesOf(input));
        return errors;
    }

    /// <inheritdoc />
    public ValidationErrors Validate(AccountChanges input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        ValidateProfile(errors, input);
        return errors;
    }

    /// <summary>True for an address shaped like <c>name@example.test</c>: one at sign, non-empty parts, a dot inside the domain, ASCII, no whitespace.</summary>
    public static bool IsAddressShaped(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var at = value.IndexOf(AtSign, StringComparison.Ordinal);
        if (at <= 0 || at != value.LastIndexOf(AtSign) || !IsPlainAscii(value))
        {
            return false;
        }

        var domain = value[(at + 1)..];
        var dot = domain.IndexOf(Dot, StringComparison.Ordinal);
        return dot > 0 && dot < domain.Length - 1;
    }

    /// <summary>True when <paramref name="value"/> is printable ASCII without whitespace.</summary>
    public static bool IsPlainAscii(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.All(character => char.IsAscii(character) && !char.IsWhiteSpace(character) && !char.IsControl(character));
    }

    private static void ValidateProfile(ValidationErrors errors, AccountChanges input)
    {
        errors.RequireText(FieldNames.Of(nameof(AccountChanges.DisplayName)), input.DisplayName, IdentityLimits.DisplayNameMaxLength);
        Optional(errors, nameof(AccountChanges.Email), input.Email, IdentityLimits.EmailMaxLength, IsAddressShaped);
        Optional(errors, nameof(AccountChanges.LoginName), input.LoginName, IdentityLimits.LoginNameMaxLength, IsPlainAscii);
        Optional(errors, nameof(AccountChanges.Upn), input.Upn, IdentityLimits.UpnMaxLength, IsAddressShaped);
        Defined(errors, nameof(AccountChanges.Kind), Enum.IsDefined(input.Kind));
        Defined(errors, nameof(AccountChanges.Binding), Enum.IsDefined(input.Binding));
        if (input.OrganizationId is <= 0)
        {
            errors.Add(FieldNames.Of(nameof(AccountChanges.OrganizationId)), ValidationMessages.InvalidFormat);
        }
    }

    private static void Optional(ValidationErrors errors, string property, string? value, int maxLength, Func<string, bool> isWellShaped)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var field = FieldNames.Of(property);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            errors.Add(field, ValidationMessages.TooLong);
        }
        else if (!isWellShaped(trimmed))
        {
            errors.Add(field, ValidationMessages.InvalidFormat);
        }
    }

    private static void Defined(ValidationErrors errors, string property, bool isDefined)
    {
        if (!isDefined)
        {
            errors.Add(FieldNames.Of(property), IdentityValidationMessages.UnknownValue);
        }
    }
}
