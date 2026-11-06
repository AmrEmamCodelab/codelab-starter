namespace Starter.Modules.ServiceRequests.Domain;

/// <summary>
/// The limits of the wizard fields. The validators and the column sizes use the same constants, and the client mirrors
/// them by hand (<c>ServiceRequestLimits</c> in <c>features/service-requests/data/service-request-rules.ts</c>); the
/// parity fixture pins both sides.
/// </summary>
public static class ServiceRequestsLimits
{
    /// <summary>The shortest title (characters, UTF-16 units, as sent).</summary>
    public const int TitleMinLength = 5;

    /// <summary>The longest title.</summary>
    public const int TitleMaxLength = 200;

    /// <summary>The shortest description.</summary>
    public const int DescriptionMinLength = 20;

    /// <summary>The longest description.</summary>
    public const int DescriptionMaxLength = 2000;

    /// <summary>The length of a contact phone (<c>05</c> and eight digits).</summary>
    public const int ContactPhoneLength = 10;

    /// <summary>A Saudi mobile number: <c>05</c> and eight more digits.</summary>
    public const string ContactPhonePattern = "^05[0-9]{8}$";

    /// <summary>The wire format of the preferred date (ISO date, no time).</summary>
    public const string PreferredDateFormat = "yyyy-MM-dd";

    /// <summary>How long a request may stay Under Review before its SLA is breached.</summary>
    public static readonly TimeSpan ReviewSla = TimeSpan.FromDays(3);

    /// <summary>How long before the due time the SLA badge turns "due soon".</summary>
    public static readonly TimeSpan ReviewDueSoonBefore = TimeSpan.FromDays(1);
}
