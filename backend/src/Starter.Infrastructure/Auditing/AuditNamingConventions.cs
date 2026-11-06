using Starter.Domain.Auditing;

namespace Starter.Infrastructure.Auditing;

/// <summary>
/// The safety net under the audit attributes: a string property without <see cref="PersonalDataAttribute"/> or
/// <see cref="NotAuditedAttribute"/> is still masked or left out when its NAME says what it holds, so a module that
/// forgot an attribute cannot put an e-mail address or a password hash into the trail. Matching is by name part,
/// ignoring case (<c>NormalizedEmail</c> is an e-mail, <c>RefreshSecretHash</c> a secret). An attribute always wins.
/// </summary>
/// <remarks>
/// Deliberately absent: generic names such as <c>Name</c>, <c>NameAr</c>, <c>Code</c> and <c>Description</c>, which
/// hold role, organization and lookup labels an auditor needs to read. Mark a personal one with the attribute.
/// </remarks>
public static class AuditNamingConventions
{
    /// <summary>Name parts of secrets: never recorded, not even masked.</summary>
    public static readonly IReadOnlyList<string> SecretNameParts = ["Password", "Secret", "Hash", "Salt", "Token", "Ticket", "Stamp"];

    /// <summary>Name parts of personal data and the masking they get, checked in this order.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, PersonalDataKind>> PersonalNameParts =
    [
        new("NationalId", PersonalDataKind.NationalId),
        new("Iqama", PersonalDataKind.NationalId),
        new("Passport", PersonalDataKind.NationalId),
        new("IdNumber", PersonalDataKind.NationalId),
        new("Email", PersonalDataKind.Email),
        new("Phone", PersonalDataKind.Phone),
        new("Mobile", PersonalDataKind.Phone),
        new("UserName", PersonalDataKind.Name),
        new("LoginName", PersonalDataKind.Name),
        new("DisplayName", PersonalDataKind.Name),
        new("FullName", PersonalDataKind.Name),
        new("FirstName", PersonalDataKind.Name),
        new("LastName", PersonalDataKind.Name),
        new("Upn", PersonalDataKind.Name),
        new("Json", PersonalDataKind.FreeText),
        new("Comment", PersonalDataKind.FreeText),
        new("Reason", PersonalDataKind.FreeText),
        new("Message", PersonalDataKind.FreeText),
        new("Note", PersonalDataKind.FreeText),
        new("Remark", PersonalDataKind.FreeText),
    ];

    /// <summary>True when <paramref name="propertyName"/> names a secret.</summary>
    public static bool IsSecret(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        return SecretNameParts.Any(part => propertyName.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The personal-data kind <paramref name="propertyName"/> names, or null.</summary>
    public static PersonalDataKind? PersonalKindOf(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        foreach (var part in PersonalNameParts)
        {
            if (propertyName.Contains(part.Key, StringComparison.OrdinalIgnoreCase))
            {
                return part.Value;
            }
        }

        return null;
    }
}
