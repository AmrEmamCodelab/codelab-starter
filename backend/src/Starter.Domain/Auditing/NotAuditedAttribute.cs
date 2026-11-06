namespace Starter.Domain.Auditing;

/// <summary>
/// Keeps an entity or a property out of the audit trail. On a class, the entity never produces an audit row (use it
/// for tables that are a trail of their own, such as record history or sign-in events, and for high-churn technical
/// rows). On a property, the entity is still audited but the property never contributes a value, and a change to it
/// alone writes no row (use it for secrets such as password or token hashes).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NotAuditedAttribute : Attribute;
