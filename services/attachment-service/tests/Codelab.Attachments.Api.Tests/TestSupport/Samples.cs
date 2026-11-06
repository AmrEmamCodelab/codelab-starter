using System.Net;
using System.Text;
using System.Text.Json;
using Codelab.Errors;

namespace Codelab.Attachments.Api.Tests.TestSupport;

/// <summary>File contents with real magic bytes, and the problem+json assertion every refusal test uses.</summary>
internal static class Samples
{
    public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% attachment service test document\n%%EOF\n");

    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    /// <summary>A PDF of <paramref name="size"/> bytes (a valid header, then padding).</summary>
    public static byte[] PdfOfSize(long size)
    {
        var content = new byte[size];
        Pdf.CopyTo(content, 0);
        return content;
    }

    /// <summary>
    /// Asserts the one Codelab error envelope: <paramref name="status"/>, problem+json, the machine
    /// <paramref name="code"/> (any code when null) and a reference code; returns the code.
    /// </summary>
    public static async Task<string> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ErrorEnvelope.ProblemJsonMediaType, response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var actualCode = body.RootElement.GetProperty(ErrorEnvelope.CodeMember).GetString();
        Assert.False(string.IsNullOrWhiteSpace(actualCode));
        if (code is not null)
        {
            Assert.Equal(code, actualCode);
        }

        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty(ErrorEnvelope.ReferenceCodeMember).GetString()));
        return actualCode!;
    }
}
