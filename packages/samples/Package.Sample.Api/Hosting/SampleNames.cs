namespace Package.Sample.Api.Hosting;

/// <summary>Names of the sample host.</summary>
public static class SampleNames
{
    /// <summary>The service name on every log line and the log file name.</summary>
    public const string ServiceName = "package-sample";
}

/// <summary>The routes the sample maps. Identifiers on the wire are PublicIds, never database ids.</summary>
public static class SampleRoutes
{
    /// <summary>Creates a record (signed, authenticated).</summary>
    public const string Records = "/api/records";

    /// <summary>Reads one record by PublicId (anonymous).</summary>
    public const string RecordById = Records + "/{id}";

    /// <summary>Sends a record to the archive upstream through the Codelab outbound client (signed, authenticated).</summary>
    public const string ArchiveRecord = RecordById + ArchiveSuffix;

    /// <summary>Ends the caller's session: its session id is revoked (signed, authenticated).</summary>
    public const string Logout = "/api/session/logout";

    /// <summary>The prefix of service-to-service routes.</summary>
    public const string InternalRecords = "/api/internal/records";

    /// <summary>A service-to-service call: authenticated by an ES256 per-request assertion only.</summary>
    public const string InternalTouch = InternalRecords + "/{id}" + TouchSuffix;

    private const string ArchiveSuffix = "/archive";
    private const string TouchSuffix = "/touch";
    private const string Separator = "/";

    /// <summary>The route of one record, for a PublicId token.</summary>
    public static string RecordFor(string token) => Records + Separator + token;

    /// <summary>The archive route of one record, for a PublicId token.</summary>
    public static string ArchiveFor(string token) => RecordFor(token) + ArchiveSuffix;

    /// <summary>The service-to-service touch route of one record, for a PublicId token.</summary>
    public static string InternalTouchFor(string token) => InternalRecords + Separator + token + TouchSuffix;
}
