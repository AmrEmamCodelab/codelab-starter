using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace Codelab.Attachments.Api.Tests.TestSupport;

/// <summary>One started host and client shared by the tests of a class (each test seals its own tickets).</summary>
public sealed class AttachmentsServiceHostFixture : IDisposable
{
    public AttachmentsServiceHostFixture()
    {
        Host = new AttachmentsServiceHost();
        Client = Host.CreateClient();
    }

    internal AttachmentsServiceHost Host { get; }

    internal HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        Host.Dispose();
    }
}

/// <summary>Credentials of other schemes, built at run time: the service must ignore every one of them.</summary>
internal static class HttpCredentials
{
    public const string ApiKeyHeader = "Api-Key";

    private const int RandomBytes = 32;

    /// <summary>A well-formed HS256 JWT with a random signature and a user's claims.</summary>
    public static readonly string UnsignedJwt = string.Join(
        '.',
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" })),
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { sub = "user-7", aud = "starter-attachments", permissions = "attachments.upload" })),
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RandomBytes)));

    /// <summary>A random API key.</summary>
    public static readonly string ApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(RandomBytes));
}
