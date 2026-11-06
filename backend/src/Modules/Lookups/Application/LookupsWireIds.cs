using Codelab.Security.PublicIds;

namespace Starter.Modules.Lookups.Application;

/// <summary>
/// The family of lookup value ids on the wire (<c>PublicId&lt;LookupItemIds&gt;</c>). One family for every type: a value
/// id is unique within its type, and every route and body that carries one names the type too. Another module that
/// receives a lookup value id (for example a contact topic) reads it with its own <see cref="IIdDomain"/> that returns
/// the same <see cref="DomainLabel"/>, then compares the number with the type's <c>&lt;Name&gt;Ids</c> constants.
/// </summary>
public sealed class LookupItemIds : IIdDomain
{
    /// <summary>The domain label bound into every lookup value PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "lookup-items";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}
