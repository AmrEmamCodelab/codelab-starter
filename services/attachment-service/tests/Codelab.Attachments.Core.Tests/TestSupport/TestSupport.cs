using System.Security.Cryptography;
using System.Text;
using Codelab.Attachments.Core.Storage;
using Codelab.Attachments.Core.Uploads;
using Codelab.Security;
using Codelab.Security.Keys;
using Codelab.Security.Uploads;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Attachments.Core.Tests.TestSupport;

/// <summary>A storage root this test creates under the temp folder and deletes on dispose.</summary>
internal sealed class TempStorageRoot : IDisposable
{
    private const string ParentFolderName = "codelab-attachments-core-tests";

    public TempStorageRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), ParentFolderName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Every file under the root (full paths).</summary>
    public IReadOnlyList<string> AllFiles() => Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories).ToList();

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }

        // The shared parent folder is left in place: deleting it raced with other test classes creating their roots.
    }
}

/// <summary>Content samples: real magic bytes followed by filler.</summary>
internal static class TestFiles
{
    public static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    public static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    public static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    public static readonly byte[] ExeMagic = "MZ"u8.ToArray();

    public static byte[] Pdf(int fillerBytes = 32) => WithFiller(PdfMagic, fillerBytes);

    public static byte[] Png(int fillerBytes = 32) => WithFiller(PngMagic, fillerBytes);

    public static byte[] Jpeg(int fillerBytes = 32) => WithFiller(JpegMagic, fillerBytes);

    public static byte[] Exe(int fillerBytes = 32) => WithFiller(ExeMagic, fillerBytes);

    public static string Sha256Hex(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static byte[] WithFiller(byte[] magic, int fillerBytes) => [.. magic, .. Encoding.ASCII.GetBytes(new string('x', fillerBytes))];
}

/// <summary>Builds multipart/form-data bodies the way a browser does.</summary>
internal sealed class MultipartBody
{
    private const string Boundary = "----codelab-test-boundary";
    private const string CrLf = "\r\n";
    private readonly List<byte> _body = [];

    public const string ContentType = "multipart/form-data; boundary=" + Boundary;

    public MultipartBody File(string fileName, byte[] content, string partName = "file")
    {
        WriteText($"--{Boundary}{CrLf}Content-Disposition: form-data; name=\"{partName}\"; filename=\"{fileName}\"{CrLf}Content-Type: application/octet-stream{CrLf}{CrLf}");
        _body.AddRange(content);
        WriteText(CrLf);
        return this;
    }

    public MultipartBody Field(string name, string value)
    {
        WriteText($"--{Boundary}{CrLf}Content-Disposition: form-data; name=\"{name}\"{CrLf}{CrLf}{value}{CrLf}");
        return this;
    }

    public UploadRequest ToRequest(string scope) => new(new MemoryStream(Build()), ContentType, scope);

    public byte[] Build()
    {
        return [.. _body, .. Encoding.ASCII.GetBytes($"--{Boundary}--{CrLf}")];
    }

    private void WriteText(string text) => _body.AddRange(Encoding.UTF8.GetBytes(text));
}

/// <summary>Wires a store and a pipeline over a temp root, a fake clock and a fresh receipt key.</summary>
internal sealed class UploadFixture : IDisposable
{
    public const string Scope = "attachments-demo|pid~attachable-records~2a|demo-document|user-1";
    private const string ReceiptKeyId = "t1";
    private const int ReceiptKeyBytes = 32;

    public UploadFixture(UploadSafetyOptions? options = null)
    {
        Options = options ?? new UploadSafetyOptions();
        Store = NewStore(Root.Path, Clock);
        Signer = new UploadReceiptSigner(NewReceiptRing(), SecurityProtocol.Create(SecurityProtocol.TestPrefix));
        Pipeline = new UploadPipeline(Store, Signer, Microsoft.Extensions.Options.Options.Create(Options), Clock, NullLogger<UploadPipeline>.Instance);
    }

    public TempStorageRoot Root { get; } = new();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero));

    public UploadSafetyOptions Options { get; }

    public FileShareAttachmentStore Store { get; }

    public UploadReceiptSigner Signer { get; }

    public UploadPipeline Pipeline { get; }

    public static FileShareAttachmentStore NewStore(string root, TimeProvider clock) =>
        new(Microsoft.Extensions.Options.Options.Create(new AttachmentStorageOptions { RootPath = root }), clock, NullLogger<FileShareAttachmentStore>.Instance);

    public static KeyRingOptions NewReceiptRing()
    {
        var ring = new KeyRingOptions { ActiveKeyId = ReceiptKeyId };
        ring.Keys.Add(new KeyEntryOptions { KeyId = ReceiptKeyId, MaterialBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(ReceiptKeyBytes)) });
        return ring;
    }

    public Task<UploadOutcome> RunAsync(MultipartBody body) => Pipeline.RunAsync(body.ToRequest(Scope), CancellationToken.None);

    public void Dispose() => Root.Dispose();
}
