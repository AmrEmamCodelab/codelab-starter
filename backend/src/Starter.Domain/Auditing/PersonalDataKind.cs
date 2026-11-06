namespace Starter.Domain.Auditing;

/// <summary>
/// What kind of personal data a property holds, which decides how the audit trail masks it (stored as int; never
/// reorder or renumber). The audit trail never stores the value itself, only its masked form.
/// </summary>
public enum PersonalDataKind
{
    /// <summary>A national id, iqama or passport number: nothing of the value is kept.</summary>
    NationalId = 1,

    /// <summary>An e-mail address: the first character of the local part and the domain are kept.</summary>
    Email = 2,

    /// <summary>A phone or mobile number: the last two digits are kept.</summary>
    Phone = 3,

    /// <summary>A person's name or login name: nothing of the value is kept.</summary>
    Name = 4,

    /// <summary>Text a person typed (a reason, a comment, a message): nothing of the value is kept.</summary>
    FreeText = 5,
}
