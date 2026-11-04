using Codelab.Security.PublicIds;

namespace Package.Sample.Api.Records;

/// <summary>The identifier family of sample records: a record id is a <c>PublicId&lt;RecordIds&gt;</c> on the wire.</summary>
public sealed class RecordIds : IIdDomain
{
    private RecordIds()
    {
    }

    /// <inheritdoc />
    public static string Domain => "record";
}

/// <summary>A second identifier family. A widget token never opens as a record (the token binds its family).</summary>
public sealed class WidgetIds : IIdDomain
{
    private WidgetIds()
    {
    }

    /// <inheritdoc />
    public static string Domain => "widget";
}
