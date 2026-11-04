using Codelab.Security.Signing;
using Codelab.Security.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Codelab.Security.Tests.Signing;

/// <summary>A host with request and response signing in a chosen mode, a known master key and a few endpoints.</summary>
internal static class SigningHost
{
    public const string Audience = "Starter.Api";
    public const string KeyId = "m1";

    public static readonly TestSession Session = new("7", "session-a", KeyId);

    public static TestHostOptions Options(SecurityEnforcementMode mode, string masterKey)
    {
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{SecuritySections.RequestSigning}:Mode"] = mode.ToString();
        options.Settings[$"{SecuritySections.RequestSigning}:Audience"] = Audience;
        options.Settings[$"{SecuritySections.RequestSigning}:MaxSignedBodyBytes"] = "1024";
        options.Settings[$"{SecuritySections.ResponseSigning}:Mode"] = mode.ToString();
        options.Settings[$"{SecuritySections.ReplayProtection}:AcknowledgeSingleInstance"] = "true";
        TestSettings.AddMaterial(options.Settings, SecuritySections.RequestSigning, KeyId, masterKey);
        options.MapEndpoints = Map;
        return options;
    }

    public static RequestSigner Signer(string masterKey) => RequestSigner.ForSession(TestProtocols.Test, masterKey, Session, Audience);

    public static HttpRequestMessage Request(HttpMethod method, string path, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        Session.ApplyTo(request);
        return request;
    }

    private static async Task<IResult> EchoAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        return TypedResults.Text(body, "application/json");
    }

    private static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me", (HttpContext context) => TypedResults.Ok(new { user = context.User.FindFirst("sub")?.Value }));
        endpoints.MapPost("/api/echo", EchoAsync);
        endpoints.MapPost("/api/login", () => TypedResults.Ok(new { ok = true })).WithSignatureExemption(SignatureExemption.WholeRequest, "Anonymous login.");
        endpoints.MapPost("/api/upload", () => TypedResults.Ok(new { stored = true })).WithSignatureExemption(SignatureExemption.BodyOnly, "Multipart upload.");
        endpoints.MapGet("/api/file", () => TypedResults.Bytes(new byte[] { 1, 2, 3 }, "application/octet-stream")).WithUnsignedResponse("File download.");
        endpoints.MapCodelabSigningKeyEndpoint();
    }
}
