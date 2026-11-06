using Codelab.Attachments.Core.Errors;
using Codelab.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Codelab.Attachments.Api.Endpoints;

/// <summary>
/// The service's refusals as the one Codelab.Errors envelope (problem+json with <c>code</c>, <c>correlationId</c> and
/// <c>referenceCode</c>). <see cref="CodedRefusal"/> has no 413 or 415 kind, so the envelope is built here with the
/// status table's sentence and written by <see cref="ProblemEnvelopeResult"/> (normalized and scrubbed like every
/// other Codelab error body).
/// </summary>
public static class AttachmentProblems
{
    /// <summary>A refusal with <paramref name="statusCode"/> and the machine <paramref name="code"/>.</summary>
    public static ProblemEnvelopeResult Refusal(int statusCode, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ErrorEnvelope.Title,
            Detail = FrameworkStatusCodes.Describe(statusCode).Detail,
        };
        problem.Extensions[ErrorEnvelope.CodeMember] = code;
        return new ProblemEnvelopeResult(problem);
    }

    /// <summary>401 <see cref="AttachmentServiceErrorCodes.TicketInvalid"/>.</summary>
    public static ProblemEnvelopeResult TicketInvalid() =>
        Refusal(StatusCodes.Status401Unauthorized, AttachmentServiceErrorCodes.TicketInvalid);

    /// <summary>503 <see cref="ErrorCodes.Retryable"/>: the replay store could not answer, so no ticket opens (fail closed).</summary>
    public static ProblemEnvelopeResult TicketStoreUnavailable() =>
        Refusal(StatusCodes.Status503ServiceUnavailable, ErrorCodes.Retryable);

    /// <summary>404 <see cref="AttachmentServiceErrorCodes.FileNotFound"/>.</summary>
    public static ProblemEnvelopeResult FileNotFound() =>
        Refusal(StatusCodes.Status404NotFound, AttachmentServiceErrorCodes.FileNotFound);

    /// <summary>413 <see cref="AttachmentServiceErrorCodes.FileTooLarge"/>: the declared body is above the upload ceiling.</summary>
    public static ProblemEnvelopeResult BodyTooLarge() =>
        Refusal(StatusCodes.Status413PayloadTooLarge, AttachmentServiceErrorCodes.FileTooLarge);
}
