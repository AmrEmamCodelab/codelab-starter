namespace Starter.Modules.Lookups.Application;

/// <summary>The Lookups module's error codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles.</summary>
public static class LookupsErrorCodes
{
    /// <summary>No lookup type has this key (404).</summary>
    public const string TypeNotFound = "LOOKUPS_TYPE_NOT_FOUND";

    /// <summary>The lookup type has no value with this id (404).</summary>
    public const string ItemNotFound = "LOOKUPS_ITEM_NOT_FOUND";

    // scaffold:error-codes
}
