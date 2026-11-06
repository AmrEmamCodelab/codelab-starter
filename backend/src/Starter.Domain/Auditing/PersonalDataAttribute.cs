namespace Starter.Domain.Auditing;

/// <summary>
/// Marks a property as personal data: the audit trail records that it changed, but only in the masked form of
/// <see cref="Kind"/> (the interceptor masks it before the row is written, so the trail never holds the value). Put it
/// next to the property, so a new personal-data column is masked from its first change.
/// </summary>
/// <param name="kind">What the property holds.</param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class PersonalDataAttribute(PersonalDataKind kind) : Attribute
{
    /// <summary>What the property holds.</summary>
    public PersonalDataKind Kind { get; } = kind;
}
