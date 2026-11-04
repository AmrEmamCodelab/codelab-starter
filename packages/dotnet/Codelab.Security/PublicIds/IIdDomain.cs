namespace Codelab.Security.PublicIds;

/// <summary>
/// One family of surrogate identifiers (records, users, documents) as a type, so <see cref="PublicId{TDomain}"/> carries
/// it in its own type identity and a <c>PublicId&lt;Users&gt;</c> cannot be assigned where a
/// <c>PublicId&lt;Records&gt;</c> is expected. The domain is bound into the AAD of every token, so a token minted for
/// one family fails authentication where another is expected.
/// <para>It is a tag, never a capability: ownership, organisation scope and permission checks stay where they are.</para>
/// </summary>
public interface IIdDomain
{
    /// <summary>
    /// The domain label (<c>[a-z][a-z0-9-]{0,31}</c>). Bound into the AAD and never on the wire; renaming it
    /// invalidates every outstanding token of the family, so treat it as part of the wire format.
    /// </summary>
    static abstract string Domain { get; }
}

/// <summary>
/// Declares that a member carries a surrogate (database identity) identifier that must never travel in numeric
/// form. It encrypts nothing: the transform is carried by the <see cref="PublicId{TDomain}"/> type and its converter.
/// Contract tests check that every marked member is typed as a PublicId. Not for lookup ids (reference data).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class WireIdAttribute : Attribute;
