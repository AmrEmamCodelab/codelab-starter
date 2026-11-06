namespace Starter.Application.Records;

/// <summary>The limits of the record lists (one owner for the paging bounds).</summary>
public static class RecordsLimits
{
    /// <summary>The page size when the caller gives none.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The largest page size.</summary>
    public const int MaxPageSize = 100;
}
