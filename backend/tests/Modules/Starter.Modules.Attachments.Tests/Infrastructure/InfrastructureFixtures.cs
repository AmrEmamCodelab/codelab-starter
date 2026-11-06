using System.Net;
using System.Security.Cryptography;
using Codelab.Security;
using Codelab.Security.Keys;
using Codelab.Security.Uploads;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Starter.Modules.Attachments.Tests.Infrastructure;

/// <summary>Test-time key material and helpers: every key is generated at run time, never a fixture.</summary>
internal static class InfrastructureFixtures
{
    public const string ServiceRoot = "http://attachments.test:5185";
    public const string FileId = "fileIdInfra00000000001";
    public const string Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string Scope = "attachments-demo|pid~attachable-records~1|demo-document|11111111-2222-3333-4444-555555555555";

    private const int KeyBytes = 32;

    public static SecurityProtocol Protocol { get; } = SecurityProtocol.Create(SecurityProtocol.TestPrefix);

    /// <summary>A fresh one-key ring (receipts enabled) under <paramref name="keyId"/>.</summary>
    public static UploadSafetyOptions ReceiptRing(string keyId = "k1", bool enabled = true)
    {
        var options = new UploadSafetyOptions { ReceiptsEnabled = enabled, ActiveKeyId = keyId };
        options.Keys.Add(NewKey(keyId));
        return options;
    }

    public static KeyEntryOptions NewKey(string keyId) =>
        new() { KeyId = keyId, MaterialBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes)) };

    public static UploadReceipt Receipt(string fileName = "report.pdf", string scope = Scope) =>
        new(Scope: scope, FileId: FileId, SizeBytes: 2048, Sha256Hex: Sha256, FileName: fileName, IssuedAtUnix: 1_700_000_000);
}

/// <summary>An <see cref="HttpMessageHandler"/> that records each request and answers from a function.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static StubHandler Status(HttpStatusCode status, string body = "") =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));

    public static StubHandler Throws(Exception exception) => new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return answer(request, cancellationToken);
    }
}

/// <summary>A minimal host environment for registration and validator tests.</summary>
internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "Starter.Modules.Attachments.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
