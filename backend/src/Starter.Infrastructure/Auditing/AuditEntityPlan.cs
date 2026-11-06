using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Starter.Domain.Auditing;

namespace Starter.Infrastructure.Auditing;

/// <summary>How the audit trail treats one property (a closed set of the interceptor; not stored).</summary>
public enum AuditPropertyTreatment
{
    /// <summary>The value is recorded as is.</summary>
    Recorded = 1,

    /// <summary>The value is an internal integer id (the row's key or a foreign key): recorded, flagged as an identifier.</summary>
    Identifier = 2,

    /// <summary>The value is personal data: only its masked form is recorded.</summary>
    Masked = 3,

    /// <summary>The value is never recorded (a secret, binary data, or <see cref="NotAuditedAttribute"/>).</summary>
    Excluded = 4,
}

/// <summary>The audit treatment of one property.</summary>
/// <param name="Name">The property name.</param>
/// <param name="Treatment">How it is recorded.</param>
/// <param name="Kind">The personal-data kind when <paramref name="Treatment"/> is <see cref="AuditPropertyTreatment.Masked"/>.</param>
public sealed record AuditPropertyPlan(string Name, AuditPropertyTreatment Treatment, PersonalDataKind? Kind);

/// <summary>The audit decision for one entity type, computed once per model (<see cref="AuditPlans"/>).</summary>
/// <param name="IsAudited">False for an entity that never produces a row.</param>
/// <param name="EntityName">The name its rows are filed under.</param>
/// <param name="Properties">The treatment of each property, by name.</param>
public sealed record AuditEntityPlan(bool IsAudited, string EntityName, IReadOnlyDictionary<string, AuditPropertyPlan> Properties)
{
    /// <summary>The shared plan of every entity that is not audited.</summary>
    public static AuditEntityPlan NotAudited { get; } = new(IsAudited: false, EntityName: string.Empty, Properties: new Dictionary<string, AuditPropertyPlan>());

    /// <summary>The treatment of <paramref name="propertyName"/>; an unknown property is excluded.</summary>
    public AuditPropertyPlan For(string propertyName) =>
        Properties.TryGetValue(propertyName, out var plan) ? plan : new AuditPropertyPlan(propertyName, AuditPropertyTreatment.Excluded, Kind: null);
}

/// <summary>
/// Resolves and remembers the <see cref="AuditEntityPlan"/> of each entity type: attributes and name conventions are
/// read once per entity type per model, never per save. EF Core keeps one model per context type, so the cache holds
/// one entry per mapped type.
/// </summary>
public static class AuditPlans
{
    private static readonly ConcurrentDictionary<IEntityType, AuditEntityPlan> Plans = new();

    /// <summary>The plan of <paramref name="entityType"/>.</summary>
    public static AuditEntityPlan For(IEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return Plans.GetOrAdd(entityType, Build);
    }

    /// <summary>The treatment of <paramref name="property"/>, in precedence order: attributes, binary, secret names, identifiers, personal names.</summary>
    public static AuditPropertyPlan PlanOf(IProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var name = property.Name;
        var member = property.PropertyInfo;
        if (member?.GetCustomAttribute<NotAuditedAttribute>(inherit: true) is not null || property.ClrType == typeof(byte[]))
        {
            return new AuditPropertyPlan(name, AuditPropertyTreatment.Excluded, Kind: null);
        }

        if (member?.GetCustomAttribute<PersonalDataAttribute>(inherit: true) is { } personal)
        {
            return new AuditPropertyPlan(name, AuditPropertyTreatment.Masked, personal.Kind);
        }

        return ConventionPlanOf(property);
    }

    private static AuditPropertyPlan ConventionPlanOf(IProperty property)
    {
        var name = property.Name;
        var isText = property.ClrType == typeof(string);
        if (isText && AuditNamingConventions.IsSecret(name))
        {
            return new AuditPropertyPlan(name, AuditPropertyTreatment.Excluded, Kind: null);
        }

        if ((property.IsKey() || property.IsForeignKey()) && IsIntegral(property.ClrType))
        {
            return new AuditPropertyPlan(name, AuditPropertyTreatment.Identifier, Kind: null);
        }

        var kind = isText ? AuditNamingConventions.PersonalKindOf(name) : null;
        return kind is null
            ? new AuditPropertyPlan(name, AuditPropertyTreatment.Recorded, Kind: null)
            : new AuditPropertyPlan(name, AuditPropertyTreatment.Masked, kind);
    }

    private static AuditEntityPlan Build(IEntityType entityType)
    {
        var clrType = entityType.ClrType;
        if (clrType == typeof(AuditEntry) || clrType.GetCustomAttribute<NotAuditedAttribute>(inherit: true) is not null)
        {
            return AuditEntityPlan.NotAudited;
        }

        var properties = entityType.GetProperties().Select(PlanOf).ToDictionary(plan => plan.Name, StringComparer.Ordinal);
        return new AuditEntityPlan(IsAudited: true, EntityName: entityType.ShortName(), Properties: properties);
    }

    private static bool IsIntegral(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(long) || underlying == typeof(int) || underlying == typeof(short);
    }
}
