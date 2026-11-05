namespace Starter.Application.Validation;

/// <summary>Field errors collected by a validator: field name to message codes (<see cref="ValidationMessages"/>).</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    /// <summary>True when no field has an error.</summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>Records <paramref name="message"/> for <paramref name="field"/> (a field may collect several).</summary>
    public ValidationErrors Add(string field, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
        return this;
    }

    /// <summary>
    /// Adds <see cref="ValidationMessages.Required"/> when <paramref name="value"/> is null or blank, or
    /// <see cref="ValidationMessages.TooLong"/> when it is longer than <paramref name="maxLength"/>.
    /// </summary>
    public ValidationErrors RequireText(string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Add(field, ValidationMessages.Required);
        }

        return value.Length > maxLength ? Add(field, ValidationMessages.TooLong) : this;
    }

    /// <summary>The errors as a map of field to message codes (a copy).</summary>
    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray(), StringComparer.Ordinal);
}
