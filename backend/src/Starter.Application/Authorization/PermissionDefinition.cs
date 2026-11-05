namespace Starter.Application.Authorization;

/// <summary>One permission of the catalog in code.</summary>
/// <param name="Key">The key, <c>&lt;module-kebab&gt;.&lt;action&gt;</c> (see <see cref="PermissionKeyFormat"/>), a constant of the owner's <c>PermissionKeys</c> class.</param>
/// <param name="Description">What holding it allows, in English, for the generated docs (screens use i18n keys).</param>
public sealed record PermissionDefinition(string Key, string Description)
{
    /// <summary>The owning module in kebab case: the key's first segment.</summary>
    public string Owner => PermissionKeyFormat.OwnerOf(Key);
}
