using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Codelab.Observability;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Starter.Application.Auditing;
using Starter.Domain.Auditing;

namespace Starter.Infrastructure.Auditing;

/// <summary>
/// Turns property values into the flat text the trail stores: scalars in invariant culture, enums as their integer
/// value (the wire convention), times in ISO 8601 round-trip form. Personal data is masked with Codelab.Observability's
/// <see cref="PiiMask"/>, so the trail and the logs mask the same way.
/// </summary>
public static class AuditValues
{
    private const string RoundTrip = "O";
    private const string ConstantSpan = "c";

    /// <summary>The text of <paramref name="value"/>, or null.</summary>
    public static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        Enum member => Convert.ToInt64(member, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        bool flag => flag ? bool.TrueString.ToLowerInvariant() : bool.FalseString.ToLowerInvariant(),
        DateTimeOffset instant => instant.ToString(RoundTrip, CultureInfo.InvariantCulture),
        DateTime time => time.ToString(RoundTrip, CultureInfo.InvariantCulture),
        DateOnly date => date.ToString(RoundTrip, CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString(RoundTrip, CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString(ConstantSpan, CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>The masked text of <paramref name="value"/> for <paramref name="kind"/>; null stays null.</summary>
    public static string? Mask(PersonalDataKind kind, object? value) => PiiMask.For(PiiKindOf(kind), Format(value));

    /// <summary>The text the trail records for <paramref name="value"/> under <paramref name="plan"/>.</summary>
    public static string? Record(AuditPropertyPlan plan, object? value)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Treatment == AuditPropertyTreatment.Masked && plan.Kind is { } kind ? Mask(kind, value) : Format(value);
    }

    /// <summary>The <see cref="PiiKind"/> that masks <paramref name="kind"/>; names, ids and free text keep nothing.</summary>
    public static PiiKind PiiKindOf(PersonalDataKind kind) => kind switch
    {
        PersonalDataKind.Email => PiiKind.Email,
        PersonalDataKind.Phone => PiiKind.Phone,
        _ => PiiKind.Hidden,
    };
}

/// <summary>The JSON of <see cref="AuditEntry.ChangesJson"/>: an array of <see cref="AuditPropertyChange"/> in camelCase.</summary>
public static class AuditChangesJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // Arabic stays readable at rest; the API re-encodes every value as a JSON string on the way out.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The JSON of <paramref name="changes"/>.</summary>
    public static string Serialize(IReadOnlyList<AuditPropertyChange> changes) => JsonSerializer.Serialize(changes, Options);

    /// <summary>The changes in <paramref name="json"/>; an empty list for blank JSON.</summary>
    /// <exception cref="JsonException">The JSON is not a change array (a corrupted row).</exception>
    public static IReadOnlyList<AuditPropertyChange> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<AuditPropertyChange>>(json, Options) ?? [];

    /// <summary>The JSON object of named parts (a composite key).</summary>
    public static string SerializeParts(IReadOnlyDictionary<string, string?> parts) => JsonSerializer.Serialize(parts, Options);
}

/// <summary>
/// The recorded key of an audited row: the text of a single key, a JSON object of the parts of a composite key, or null
/// when the entity has no primary key.
/// </summary>
public static class AuditKeys
{
    /// <summary>The key of <paramref name="entry"/> (original values for a deleted row, current values otherwise).</summary>
    public static string? Of(EntityEntry entry, bool useOriginalValues)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return null;
        }

        if (key.Properties.Count == 1)
        {
            return AuditValues.Format(ValueOf(entry, key.Properties[0].Name, useOriginalValues));
        }

        var parts = key.Properties.ToDictionary(
            property => property.Name,
            property => AuditValues.Format(ValueOf(entry, property.Name, useOriginalValues)),
            StringComparer.Ordinal);
        return AuditChangesJson.SerializeParts(parts);
    }

    private static object? ValueOf(EntityEntry entry, string propertyName, bool useOriginalValues)
    {
        var property = entry.Property(propertyName);
        return useOriginalValues ? property.OriginalValue : property.CurrentValue;
    }
}
