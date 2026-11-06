using System.Globalization;
using Codelab.Security.PublicIds;
using Starter.Application.Users;
using Starter.Domain.Exceptions;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Application;

/// <summary>
/// The one owner of the upload scope: the upload ticket's subject, echoed by the attachment service as the receipt's
/// scope, and compared again when the receipt is linked. Format:
/// <c>{recordType}|{recordPublicId}|{typeKey}|{userSubject}</c>, so a receipt links only to the record, type and user its
/// ticket was issued for. The receipt is signed, not encrypted, and reaches the browser, so the record appears only as
/// its PublicId (family <see cref="AttachableRecordIds"/>, the token the browser already has in the route), never as
/// the internal number; the user subject is the caller's own, already in their access token.
/// </summary>
public sealed class UploadScopes(IPublicIdCodec codec)
{
    /// <summary>The separator between the scope parts (never part of a kebab key, a PublicId token or a GUID).</summary>
    public const char Separator = '|';

    /// <summary>The GUID format of the user subject part (hyphenated, lower case).</summary>
    public const string SubjectFormat = "D";

    private const int PartCount = 4;
    private const int RecordTypePart = 0;
    private const int RecordPart = 1;
    private const int TypeKeyPart = 2;
    private const int SubjectPart = 3;

    /// <summary>The scope of an upload to <paramref name="owner"/>'s record under type <paramref name="typeKey"/> by <paramref name="userSubject"/>.</summary>
    public string For(AttachmentOwner owner, string typeKey, Guid userSubject)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        return string.Join(
            Separator,
            owner.RecordType,
            codec.Protect(AttachableRecordIds.Domain, owner.RecordId),
            typeKey,
            Subject(userSubject));
    }

    /// <summary>
    /// True when <paramref name="actual"/> is the scope of exactly this record, type and user: the record part must
    /// authenticate as a PublicId of <paramref name="owner"/>'s record (so a PublicId key rotation between ticket and
    /// link does not refuse the receipt), every other part must match ordinally.
    /// </summary>
    public bool Matches(string? actual, AttachmentOwner owner, string typeKey, Guid userSubject)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var parts = actual?.Split(Separator);
        return parts is { Length: PartCount }
            && string.Equals(parts[RecordTypePart], owner.RecordType, StringComparison.Ordinal)
            && codec.TryUnprotect(AttachableRecordIds.Domain, parts[RecordPart], out var recordId)
            && recordId == owner.RecordId
            && string.Equals(parts[TypeKeyPart], typeKey, StringComparison.Ordinal)
            && string.Equals(parts[SubjectPart], Subject(userSubject), StringComparison.Ordinal);
    }

    private static string Subject(Guid userSubject) => userSubject.ToString(SubjectFormat, CultureInfo.InvariantCulture);
}

/// <summary>The signed-in user acting on attachments, and the clock that stamps their changes.</summary>
public sealed class AttachmentActor(ICurrentUserContext currentUser, TimeProvider clock)
{
    /// <summary>The user's token subject.</summary>
    /// <exception cref="AccessDeniedException">The request carries no user (the endpoints require one).</exception>
    public Guid RequireSubject() =>
        currentUser.SubjectId ?? throw new AccessDeniedException("Attachments need a signed-in user.");

    /// <summary>The user and the current time, for a link or a removal.</summary>
    /// <exception cref="AccessDeniedException">The request carries no user.</exception>
    public AttachmentStamp Stamp() => new(RequireSubject(), clock.GetUtcNow());
}
