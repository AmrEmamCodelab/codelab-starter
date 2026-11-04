namespace Codelab.Observability;

/// <summary>
/// Marks a property as personal data. When an object is destructured into a log event (<c>{@Value}</c>),
/// <see cref="PiiMaskingDestructuringPolicy"/> writes the property masked by <see cref="Kind"/> instead of its value.
/// A non-string value is always written as <see cref="PiiMask.Hidden"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class PiiAttribute(PiiKind kind = PiiKind.Hidden) : Attribute
{
    /// <summary>How the value is masked.</summary>
    public PiiKind Kind { get; } = kind;
}
