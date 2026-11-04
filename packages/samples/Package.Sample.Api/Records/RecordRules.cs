namespace Package.Sample.Api.Records;

/// <summary>Validation of <see cref="CreateRecordRequest"/>: every field error at once, keyed by member name.</summary>
public static class RecordRules
{
    /// <summary>The longest title accepted.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>The error key of the title.</summary>
    public const string TitleField = "title";

    /// <summary>The error key of the contact e-mail.</summary>
    public const string ContactEmailField = "contactEmail";

    /// <summary>The message key of a missing value.</summary>
    public const string Required = "REQUIRED";

    /// <summary>The message key of a value over its length limit.</summary>
    public const string TooLong = "TOO_LONG";

    /// <summary>The message key of a malformed e-mail address.</summary>
    public const string NotAnEmail = "NOT_AN_EMAIL";

    private const char At = '@';

    /// <summary>The field errors of <paramref name="request"/>; empty when it is valid.</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(CreateRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors[TitleField] = [Required];
        }
        else if (request.Title.Trim().Length > MaxTitleLength)
        {
            errors[TitleField] = [TooLong];
        }

        if (string.IsNullOrWhiteSpace(request.ContactEmail))
        {
            errors[ContactEmailField] = [Required];
        }
        else if (!IsEmailShaped(request.ContactEmail.Trim()))
        {
            errors[ContactEmailField] = [NotAnEmail];
        }

        return errors;
    }

    private static bool IsEmailShaped(string value)
    {
        var at = value.IndexOf(At, StringComparison.Ordinal);
        return at > 0 && at == value.LastIndexOf(At) && at < value.Length - 1;
    }
}
