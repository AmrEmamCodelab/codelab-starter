using Codelab.Security.PublicIds;

namespace Package.Sample.Api.Records;

/// <summary>A record as the API returns it: the id is an opaque PublicId token.</summary>
public sealed record RecordResponse(PublicId<RecordIds> Id, string Title, bool Archived)
{
    /// <summary>The wire shape of a stored record.</summary>
    public static RecordResponse From(SampleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new RecordResponse(PublicId<RecordIds>.From(record.Id), record.Title, record.Archived);
    }
}

/// <summary>Creates a record. The contact e-mail is personal data: it is never logged unmasked.</summary>
public sealed record CreateRecordRequest(string? Title, string? ContactEmail);

/// <summary>What a service-to-service call returns: the record and the service that asserted the call.</summary>
public sealed record TouchResponse(PublicId<RecordIds> Id, string CalledBy);
