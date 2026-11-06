using System.Text.Json;
using Codelab.Attachments.Api.Endpoints;
using Codelab.Attachments.Core.Errors;
using Codelab.Errors;
using Codelab.Security.Uploads;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Codelab.Attachments.Api.Tests.Endpoints;

public sealed class EndpointUnitTests
{
    [Fact]
    public void The_body_ceiling_is_the_total_cap_plus_framing_per_part_and_the_closing_boundary()
    {
        var options = new UploadSafetyOptions { MaxTotalBytes = 1_000_000, MaxFileCount = 4 };

        Assert.Equal(1_000_000 + (5 * UploadBodyCeiling.PartFramingBytes), UploadBodyCeiling.For(options));
    }

    [Theory]
    [InlineData("scan.pdf", UploadContentTypes.Pdf)]
    [InlineData("PHOTO.JPG", UploadContentTypes.Jpeg)]
    [InlineData("photo.jpeg", UploadContentTypes.Jpeg)]
    [InlineData("chart.png", UploadContentTypes.Png)]
    [InlineData("page.html", "application/octet-stream")]
    [InlineData("attachment", "application/octet-stream")]
    public void The_download_type_comes_from_the_one_allowlist(string fileName, string expected)
    {
        Assert.Equal(expected, AttachmentsEndpoints.DownloadContentType(fileName));
    }

    [Theory]
    [InlineData(StatusCodes.Status401Unauthorized, AttachmentServiceErrorCodes.TicketInvalid)]
    [InlineData(StatusCodes.Status413PayloadTooLarge, AttachmentServiceErrorCodes.FileTooLarge)]
    [InlineData(StatusCodes.Status415UnsupportedMediaType, AttachmentServiceErrorCodes.SignatureMismatch)]
    public void A_refusal_is_the_codelab_envelope(int status, string code)
    {
        var result = AttachmentProblems.Refusal(status, code);

        Assert.Equal(status, result.StatusCode);
        Assert.Equal(ErrorEnvelope.ProblemJsonMediaType, result.ContentType);
        Assert.Equal(ErrorEnvelope.Title, result.Value.Title);
        Assert.Equal(code, result.Value.Extensions[ErrorEnvelope.CodeMember]);
    }

    [Fact]
    public void The_named_refusals_carry_their_status_and_code()
    {
        AssertRefusal(AttachmentProblems.TicketInvalid(), StatusCodes.Status401Unauthorized, AttachmentServiceErrorCodes.TicketInvalid);
        AssertRefusal(AttachmentProblems.TicketStoreUnavailable(), StatusCodes.Status503ServiceUnavailable, ErrorCodes.Retryable);
        AssertRefusal(AttachmentProblems.FileNotFound(), StatusCodes.Status404NotFound, AttachmentServiceErrorCodes.FileNotFound);
        AssertRefusal(AttachmentProblems.BodyTooLarge(), StatusCodes.Status413PayloadTooLarge, AttachmentServiceErrorCodes.FileTooLarge);
    }

    [Fact]
    public async Task A_refusal_writes_problem_json_with_a_reference_code()
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        await AttachmentProblems.FileNotFound().ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(AttachmentServiceErrorCodes.FileNotFound, body.RootElement.GetProperty(ErrorEnvelope.CodeMember).GetString());
        Assert.True(body.RootElement.TryGetProperty(ErrorEnvelope.ReferenceCodeMember, out _));
    }

    private static void AssertRefusal(ProblemEnvelopeResult result, int status, string code)
    {
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(code, ((ProblemDetails)result.Value).Extensions[ErrorEnvelope.CodeMember]);
    }
}
