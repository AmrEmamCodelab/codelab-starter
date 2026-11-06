namespace Starter.Application.Records;

/// <summary>
/// One field error of a record submit check: the camelCase wire name of the field (<c>FieldNames.Of</c>) and one
/// message code (<c>ValidationMessages</c>). A field may appear in several errors, one per code.
/// </summary>
/// <param name="Field">The camelCase field name.</param>
/// <param name="Code">The message code the client translates (<c>validation.&lt;CODE&gt;</c>).</param>
/// <exception cref="ArgumentException">The field or the code is blank.</exception>
public sealed record FieldError(string Field, string Code)
{
    /// <summary>The camelCase field name.</summary>
    public string Field { get; } = string.IsNullOrWhiteSpace(Field)
        ? throw new ArgumentException("A field error names its field.", nameof(Field))
        : Field;

    /// <summary>The message code.</summary>
    public string Code { get; } = string.IsNullOrWhiteSpace(Code)
        ? throw new ArgumentException("A field error carries a message code.", nameof(Code))
        : Code;
}
