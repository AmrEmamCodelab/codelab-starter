namespace Starter.Application.Results;

/// <summary>
/// An expected business "no" returned as a value by a use case (ERROR-HANDLING row 5). The endpoint maps it once into
/// the problem+json envelope. The code is the whole contract: the client renders <c>errors.&lt;CODE&gt;</c>.
/// </summary>
public sealed record Refusal
{
    private static readonly IReadOnlyDictionary<string, string[]> NoFieldErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);

    private Refusal(RefusalCategory category, string code, IReadOnlyDictionary<string, string[]> fieldErrors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Category = category;
        Code = code;
        FieldErrors = fieldErrors;
    }

    /// <summary>The kind of refusal.</summary>
    public RefusalCategory Category { get; }

    /// <summary>The module or platform error code.</summary>
    public string Code { get; }

    /// <summary>Field name to message codes; empty unless a validator refused the input.</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; }

    /// <summary>A validator's refusal with every field error, under <paramref name="code"/>.</summary>
    public static Refusal Invalid(IReadOnlyDictionary<string, string[]> fieldErrors, string code)
    {
        ArgumentNullException.ThrowIfNull(fieldErrors);
        return new Refusal(RefusalCategory.Invalid, code, fieldErrors);
    }

    /// <summary>A validator's refusal under <see cref="PlatformErrorCodes.Validation"/>.</summary>
    public static Refusal Invalid(Validation.ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return new Refusal(RefusalCategory.Invalid, PlatformErrorCodes.Validation, errors.ToDictionary());
    }

    /// <summary>An invalid request described by a module code alone (no field errors).</summary>
    public static Refusal Invalid(string code) => new(RefusalCategory.Invalid, code, NoFieldErrors);

    /// <summary>403 with <paramref name="code"/>.</summary>
    public static Refusal Forbidden(string code) => new(RefusalCategory.Forbidden, code, NoFieldErrors);

    /// <summary>404 with <paramref name="code"/>.</summary>
    public static Refusal NotFound(string code) => new(RefusalCategory.NotFound, code, NoFieldErrors);

    /// <summary>409 with <paramref name="code"/>.</summary>
    public static Refusal Conflict(string code) => new(RefusalCategory.Conflict, code, NoFieldErrors);

    /// <summary>422 with <paramref name="code"/>.</summary>
    public static Refusal Unprocessable(string code) => new(RefusalCategory.Unprocessable, code, NoFieldErrors);
}
