using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace Codelab.Observability;

/// <summary>
/// The root destructuring policy every Codelab host installs: an object destructured into a log event
/// (<c>{@Value}</c>) whose type has a <see cref="PiiAttribute"/> property is written with that property masked
/// (<see cref="PiiMask.For"/>); its other public properties are destructured as usual. A type with no marked
/// property is left to Serilog's default, so the policy costs one cached lookup per type.
/// <para>
/// This masks what a developer marked; it does not find personal data on its own. Log identifiers, not content:
/// the property catalogue (<see cref="LogPropertyNames"/>) and the <see cref="UserIdHash"/> are the defaults.
/// </para>
/// </summary>
public sealed class PiiMaskingDestructuringPolicy : IDestructuringPolicy
{
    private readonly ConcurrentDictionary<Type, PropertyPlan[]?> _plans = new();

    /// <inheritdoc />
    public bool TryDestructure(
        object value, ILogEventPropertyValueFactory propertyValueFactory, [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        var plan = _plans.GetOrAdd(value.GetType(), PlanFor);
        if (plan is null)
        {
            result = null;
            return false;
        }

        var properties = plan.Select(p => new LogEventProperty(p.Property.Name, ValueOf(p, value, propertyValueFactory)));
        result = new StructureValue(properties, value.GetType().Name);
        return true;
    }

    private static LogEventPropertyValue ValueOf(PropertyPlan plan, object owner, ILogEventPropertyValueFactory factory)
    {
        var raw = plan.Property.GetValue(owner);
        if (plan.Kind is not { } kind)
        {
            return factory.CreatePropertyValue(raw, destructureObjects: true);
        }

        return new ScalarValue(raw switch
        {
            null => null,
            string text => PiiMask.For(kind, text),
            _ => PiiMask.Hidden,
        });
    }

    /// <summary>The readable public properties of <paramref name="type"/>, or null when none is marked.</summary>
    private static PropertyPlan[]? PlanFor(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => new PropertyPlan(p, p.GetCustomAttribute<PiiAttribute>(inherit: true)?.Kind))
            .ToArray();

        return properties.Any(p => p.Kind is not null) ? properties : null;
    }

    private sealed record PropertyPlan(PropertyInfo Property, PiiKind? Kind);
}
