namespace Starter.Application.Settings;

/// <summary>
/// How a runtime setting's text value is read and validated. Stored and sent on the wire as the integer value; the
/// client labels it from its ar and en bundles.
/// </summary>
public enum SettingValueType
{
    /// <summary>Free text (may be empty).</summary>
    Text = 1,

    /// <summary>A 32-bit integer in invariant culture (<c>-5</c>, <c>20</c>).</summary>
    WholeNumber = 2,

    /// <summary><c>true</c> or <c>false</c> (stored lower-case).</summary>
    Boolean = 3,

    /// <summary>A non-negative whole number of seconds; read as a <see cref="TimeSpan"/>.</summary>
    DurationSeconds = 4,
}
