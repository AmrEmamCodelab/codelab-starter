namespace Codelab.Observability;

/// <summary>
/// The masked forms a log line may carry in place of a contact detail, so an on-call reader can tell two values
/// apart ("was it the same address?") without a retained sink holding the value itself. Masking is for LOGS only:
/// nothing here is reversible or unique, and nothing may key on it. Every method returns <c>null</c> for a null or
/// blank input, so a property stays absent-valued rather than reading as a value.
/// </summary>
public static class PiiMask
{
    /// <summary>What a hidden part reads as.</summary>
    public const string Hidden = "***";

    private const int PhoneDigitsKept = 2;
    private const char At = '@';
    private const char DomainSeparator = '\\';
    private static readonly char[] UrlTail = ['?', '#'];

    /// <summary>The first character of the local part and the whole domain: <c>person@example.test</c> becomes <c>p***@example.test</c>.</summary>
    public static string? Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim();
        var at = trimmed.LastIndexOf(At);
        if (at < 0)
        {
            return Hidden;
        }

        return at == 0 ? Hidden + trimmed[at..] : trimmed[0] + Hidden + trimmed[at..];
    }

    /// <summary>The last two digits only: <c>+44 20 7946 0958</c> becomes <c>***58</c>; length and country code are not revealed.</summary>
    public static string? Phone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = phone.Where(char.IsAsciiDigit).ToArray();
        return digits.Length <= PhoneDigitsKept ? Hidden : Hidden + new string(digits[^PhoneDigitsKept..]);
    }

    /// <summary>
    /// A login identifier: an <c>@</c> makes it an <see cref="Email"/>; otherwise the first character of the account is
    /// kept and any <c>DOMAIN\</c> prefix stays whole (<c>CORP\jane.doe</c> becomes <c>CORP\j***</c>).
    /// </summary>
    public static string? LoginName(string? loginName)
    {
        if (string.IsNullOrWhiteSpace(loginName))
        {
            return null;
        }

        var trimmed = loginName.Trim();
        if (trimmed.Contains(At, StringComparison.Ordinal))
        {
            return Email(trimmed);
        }

        var account = trimmed.LastIndexOf(DomainSeparator) + 1;
        return account == trimmed.Length ? trimmed[..account] + Hidden : trimmed[..account] + trimmed[account] + Hidden;
    }

    /// <summary>A URL up to its query or fragment: a list filter's query routinely carries what was typed into it.</summary>
    public static string? UrlWithoutQuery(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var end = url.IndexOfAny(UrlTail);
        return end < 0 ? url : url[..end];
    }

    /// <summary>A recipient of either kind: an <c>@</c> makes it an <see cref="Email"/>, anything else a <see cref="Phone"/>.</summary>
    public static string? Recipient(string? recipient) =>
        recipient is not null && recipient.Contains(At, StringComparison.Ordinal) ? Email(recipient) : Phone(recipient);

    /// <summary>The masked form of <paramref name="value"/> for <paramref name="kind"/>; <see cref="PiiKind.Hidden"/> keeps nothing.</summary>
    public static string? For(PiiKind kind, string? value) => kind switch
    {
        PiiKind.Email => Email(value),
        PiiKind.Phone => Phone(value),
        PiiKind.LoginName => LoginName(value),
        PiiKind.Url => UrlWithoutQuery(value),
        PiiKind.Recipient => Recipient(value),
        _ => string.IsNullOrWhiteSpace(value) ? null : Hidden,
    };
}
