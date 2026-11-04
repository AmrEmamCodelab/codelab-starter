using System.Security.Claims;
using Codelab.Errors;
using Codelab.Observability;
using Codelab.Security.PublicIds;
using Codelab.Security.Sessions;
using Microsoft.Extensions.Options;
using Package.Sample.Api.Archive;
using Package.Sample.Api.Authentication;
using Package.Sample.Api.Hosting;

namespace Package.Sample.Api.Records;

/// <summary>
/// The sample endpoints. They hold no logic beyond mapping: the packages do the security, error and logging work.
/// </summary>
public static class RecordEndpoints
{
    /// <summary>Maps every sample endpoint.</summary>
    public static IEndpointRouteBuilder MapRecordEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet(SampleRoutes.RecordById, GetRecord).AllowAnonymous();
        endpoints.MapPost(SampleRoutes.Records, CreateRecord).RequireAuthorization();
        endpoints.MapPost(SampleRoutes.ArchiveRecord, ArchiveRecordAsync).RequireAuthorization();
        endpoints.MapPost(SampleRoutes.Logout, LogoutAsync).RequireAuthorization();
        endpoints.MapPost(SampleRoutes.InternalTouch, TouchRecord).RequireAuthorization(SampleAuthentication.InternalServicePolicy);
        return endpoints;
    }

    internal static IResult GetRecord(PublicId<RecordIds> id, RecordStore store) =>
        store.Find(id.Value) is { } record ? TypedResults.Ok(RecordResponse.From(record)) : CodedRefusal.NotFound().ToResult();

    internal static IResult CreateRecord(CreateRecordRequest request, RecordStore store, ILogger<RecordStore> logger)
    {
        var errors = RecordRules.Validate(request);
        if (errors.Count > 0)
        {
            return CodedRefusal.Invalid(errors).ToResult();
        }

        var record = store.Add(request.Title!);
        var maskedEmail = PiiMask.Email(request.ContactEmail) ?? string.Empty;
        SampleLog.RecordCreated(logger, record.Id, maskedEmail);
        return TypedResults.Ok(RecordResponse.From(record));
    }

    internal static async Task<IResult> ArchiveRecordAsync(PublicId<RecordIds> id, RecordStore store, ArchiveClient archive, CancellationToken cancellationToken)
    {
        if (store.Find(id.Value) is null)
        {
            return CodedRefusal.NotFound().ToResult();
        }

        await archive.ArchiveAsync(id, cancellationToken);
        store.MarkArchived(id.Value);
        return TypedResults.NoContent();
    }

    internal static async Task<IResult> LogoutAsync(
        ClaimsPrincipal user, ISessionRevocationStore revocations, TimeProvider clock, IOptions<SampleAuthenticationOptions> options, CancellationToken cancellationToken)
    {
        var sessionId = user.FindFirst(SecurityClaimTypes.SessionId)?.Value;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return CodedRefusal.Forbidden().ToResult();
        }

        var expiresAt = clock.GetUtcNow() + options.Value.SessionLifetime;
        await revocations.RevokeAsync(new SessionRevocation(sessionId, expiresAt), cancellationToken);
        return TypedResults.NoContent();
    }

    internal static IResult TouchRecord(PublicId<RecordIds> id, ClaimsPrincipal caller, RecordStore store) =>
        store.Find(id.Value) is null
            ? CodedRefusal.NotFound().ToResult()
            : TypedResults.Ok(new TouchResponse(id, caller.FindFirst(SecurityClaimTypes.Service)?.Value ?? string.Empty));
}

/// <summary>The sample's log lines: identifiers only; personal data is masked before it reaches a template.</summary>
internal static partial class SampleLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Record {RecordId} created for {ContactEmailMasked}")]
    public static partial void RecordCreated(ILogger logger, long recordId, string contactEmailMasked);
}
