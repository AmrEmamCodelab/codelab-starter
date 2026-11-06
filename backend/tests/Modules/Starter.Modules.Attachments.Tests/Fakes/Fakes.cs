using System.Globalization;
using Codelab.Security.PublicIds;
using Starter.Application.Attachments;
using Starter.Application.Authorization;
using Starter.Application.Users;
using Starter.Domain.Attachments;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Tests.Fakes;

/// <summary>Builders for the test data: the demo type, a second type, stored files and links.</summary>
internal static class TestData
{
    public const string OtherRecordType = "other-records";
    public const string OptionalTypeKey = "optional-photo";
    public const int OptionalTypeId = 2;
    public const long RecordId = DemoRecord.RecordId;
    public const string Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    public static readonly Guid User = Guid.Parse("11111111-2222-3333-4444-555555555555");
    public static readonly Guid OtherUser = Guid.Parse("99999999-8888-7777-6666-555555555555");

    public static AttachableRecord Demo => new(AttachableRecordTypes.Demo, RecordId);

    public static ServiceAttachment DemoType(int maxFiles = 2, bool isRequired = true, bool isActive = true) =>
        ServiceAttachment.Seeded(new ServiceAttachmentSeed(
            Id: AttachmentTypeIds.DemoDocument,
            Key: AttachmentTypeKeys.DemoDocument,
            Names: new AttachmentTypeNames(Ar: "مستند تجريبي", En: "Demo document"),
            RecordType: AttachableRecordTypes.Demo,
            Rules: new AttachmentTypeRules(IsRequired: isRequired, MaxFiles: maxFiles, AllowedExtensions: [".pdf", ".png"]),
            IsActive: isActive));

    public static ServiceAttachment OptionalType(bool isRequired = false) =>
        ServiceAttachment.Seeded(new ServiceAttachmentSeed(
            Id: OptionalTypeId,
            Key: OptionalTypeKey,
            Names: new AttachmentTypeNames(Ar: "صورة", En: "Photo"),
            RecordType: AttachableRecordTypes.Demo,
            Rules: new AttachmentTypeRules(IsRequired: isRequired, MaxFiles: 1, AllowedExtensions: ["jpg"])));

    public static StoredFile File(string fileId = "fileIdAlpha00000000001", string fileName = "report.pdf") =>
        new(FileId: fileId, FileName: fileName, SizeBytes: 1024, Sha256Hex: Sha256, ContentType: "application/pdf");

    public static RecordAttachment Link(ServiceAttachment type, string fileId, long recordId = RecordId, string? recordType = null) =>
        RecordAttachment.Link(
            new AttachmentOwner(recordType ?? type.RecordType, recordId, type.Id),
            File(fileId),
            new AttachmentStamp(User, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)));

    public static UploadScopes Scopes { get; } = new(new FakePublicIdCodec());

    public static string ScopeFor(ServiceAttachment type, Guid user, long recordId = RecordId) =>
        Scopes.For(new AttachmentOwner(type.RecordType, recordId, type.Id), type.Key, user);
}

/// <summary>An in-memory <see cref="IAttachmentStore"/> that numbers new links and counts saves.</summary>
internal sealed class FakeAttachmentStore : IAttachmentStore
{
    private long _nextId = 100;

    public List<ServiceAttachment> Types { get; } = [];

    public List<RecordAttachment> Attachments { get; } = [];

    public int Saves { get; private set; }

    public Task<IReadOnlyList<ServiceAttachment>> ListTypesAsync(string recordType, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ServiceAttachment>>([.. Types.Where(type => type.RecordType == recordType).OrderBy(type => type.Key, StringComparer.Ordinal)]);

    public Task<ServiceAttachment?> FindTypeAsync(string recordType, string typeKey, CancellationToken cancellationToken) =>
        Task.FromResult(Types.SingleOrDefault(type => type.RecordType == recordType && type.Key == typeKey));

    public Task<IReadOnlyList<RecordAttachment>> ListLiveAsync(string recordType, long recordId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecordAttachment>>([.. Attachments
            .Where(attachment => attachment.IsLive && attachment.RecordType == recordType && attachment.RecordId == recordId)
            .OrderBy(attachment => attachment.UploadedAt)
            .ThenBy(attachment => attachment.Id)]);

    public Task<RecordAttachment?> FindAsync(long attachmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Attachments.SingleOrDefault(attachment => attachment.Id == attachmentId));

    public Task<RecordAttachment?> FindByFileIdAsync(string fileId, CancellationToken cancellationToken) =>
        Task.FromResult(Attachments.SingleOrDefault(attachment => attachment.FileId == fileId));

    public void Add(RecordAttachment attachment)
    {
        Stored(attachment);
    }

    /// <summary>Adds an attachment as if it had been saved earlier, with a generated id.</summary>
    public RecordAttachment Stored(RecordAttachment attachment)
    {
        typeof(RecordAttachment).GetProperty(nameof(RecordAttachment.Id))!.SetValue(attachment, _nextId++);
        Attachments.Add(attachment);
        return attachment;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.FromResult(1);
    }
}

/// <summary>A record access whose answers the test sets, and that records what it was asked.</summary>
internal sealed class FakeRecordAccess(string recordType = AttachableRecordTypes.Demo) : IAttachableRecordAccess
{
    public bool CanView { get; set; } = true;

    public bool CanChange { get; set; } = true;

    public List<(RecordAccessKind Kind, long RecordId)> Asked { get; } = [];

    public string RecordType => recordType;

    public Task<bool> CanViewAsync(long recordId, CancellationToken cancellationToken)
    {
        Asked.Add((RecordAccessKind.View, recordId));
        return Task.FromResult(CanView && recordId == TestData.RecordId);
    }

    public Task<bool> CanChangeAsync(long recordId, CancellationToken cancellationToken)
    {
        Asked.Add((RecordAccessKind.Change, recordId));
        return Task.FromResult(CanChange && recordId == TestData.RecordId);
    }
}

/// <summary>A receipt verifier that answers the upload registered for a receipt string, or null (a tampered receipt).</summary>
internal sealed class FakeReceiptVerifier : IAttachmentReceiptVerifier
{
    public Dictionary<string, VerifiedUpload> Receipts { get; } = new(StringComparer.Ordinal);

    public VerifiedUpload? Verify(string receipt) => Receipts.GetValueOrDefault(receipt);
}

/// <summary>A storage client over a dictionary of file bytes; can simulate an outage.</summary>
internal sealed class FakeStorageClient : IAttachmentStorageClient
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public bool Unavailable { get; set; }

    public Task<Stream?> OpenReadAsync(string fileId, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        return Task.FromResult<Stream?>(Files.TryGetValue(fileId, out var bytes) ? new MemoryStream(bytes) : null);
    }

    public Task<bool> DeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        return Task.FromResult(Files.Remove(fileId));
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
        {
            throw new AttachmentStorageUnavailableException(new IOException("down"));
        }
    }
}

/// <summary>Records the file ids enqueued for deletion, and the store saves seen at that moment.</summary>
internal sealed class FakeFileCleanup(FakeAttachmentStore store) : IAttachmentFileCleanup
{
    public List<(string FileId, int SavesBefore)> Enqueued { get; } = [];

    public Task EnqueueDeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        Enqueued.Add((fileId, store.Saves));
        return Task.CompletedTask;
    }
}

/// <summary>Issues predictable tickets and records the scopes it sealed.</summary>
internal sealed class FakeTicketIssuer : IUploadTicketIssuer
{
    public const string Header = "X-Test-Upload-Ticket";
    public const string Url = "http://localhost:5185/api/uploads";

    public static readonly DateTimeOffset ExpiresAt = new(2026, 1, 2, 3, 9, 5, TimeSpan.Zero);

    public List<string> Scopes { get; } = [];

    public IssuedUploadTicket Issue(string scope)
    {
        Scopes.Add(scope);
        return new IssuedUploadTicket("t1.test." + Scopes.Count, Header, Url, ExpiresAt);
    }
}

/// <summary>The current user, or an anonymous request.</summary>
internal sealed class FakeCurrentUser(Guid? subject) : ICurrentUserContext
{
    public bool IsAuthenticated => subject is not null;

    public Guid? SubjectId => subject;

    public string? SessionId => subject is null ? null : "session";
}

/// <summary>An organization scope that allows exactly the permission keys it holds.</summary>
internal sealed class FakeOrganizationScope(params string[] held) : IOrganizationScope
{
    public Task<ScopeDecision> ScopeForAsync(string permissionKey, CancellationToken cancellationToken) =>
        Task.FromResult(held.Contains(permissionKey, StringComparer.Ordinal)
            ? new ScopeDecision(Allowed: true, Scope: PermissionScope.Global, OrganizationId: null)
            : ScopeDecision.Denied);
}

/// <summary>
/// A keyless <see cref="IPublicIdCodec"/>: <c>pid~{domain}~{id in hex}</c>, so a test can tell a token from the decimal
/// id and a token of another domain or a garbled one does not authenticate.
/// </summary>
internal sealed class FakePublicIdCodec : IPublicIdCodec
{
    private const string Prefix = "pid~";
    private const char Separator = '~';
    private const string HexFormat = "x";

    public string Protect(string domain, long id) => Prefix + domain + Separator + id.ToString(HexFormat, CultureInfo.InvariantCulture);

    public bool TryUnprotect(string domain, string? token, out long id)
    {
        id = 0;
        var head = Prefix + domain + Separator;
        return token is not null && token.StartsWith(head, StringComparison.Ordinal)
            && long.TryParse(token[head.Length..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out id);
    }
}
