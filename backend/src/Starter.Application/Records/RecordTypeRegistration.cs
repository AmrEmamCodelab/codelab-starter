using Starter.Application.Authorization;
using Starter.Application.Numbering;
using Starter.Domain.Records;

namespace Starter.Application.Records;

/// <summary>
/// What the Records kernel needs to know about one record type, registered by the module that owns it (the only place
/// the record type's vocabulary lives). Validated on construction, so a malformed registration fails at startup.
/// </summary>
/// <param name="RecordType">The record type key (<see cref="RecordTypeKeyFormat"/>), for example <c>leave-request</c>.</param>
/// <param name="NumberPrefix">The reference number prefix (<see cref="RequestNumberFormat.IsValidPrefix"/>: 2-6 uppercase letters).</param>
/// <param name="QueuePermissionKey">The permission that shows the record type's work queue to staff (<see cref="PermissionKeyFormat"/>).</param>
/// <param name="ViewOwnPermissionKey">The permission that lets a requester list and open their own records.</param>
/// <param name="AttachableRecordType">The attachment record type of these records (<see cref="RecordTypeKeyFormat"/>; usually the record type key).</param>
/// <exception cref="ArgumentException">A key, the prefix or a permission key is malformed.</exception>
public sealed record RecordTypeRegistration(
    string RecordType,
    string NumberPrefix,
    string QueuePermissionKey,
    string ViewOwnPermissionKey,
    string AttachableRecordType)
{
    /// <summary>The record type key.</summary>
    public string RecordType { get; } = RecordTypeKeyFormat.EnsureValid(RecordType, nameof(RecordType));

    /// <summary>The reference number prefix.</summary>
    public string NumberPrefix { get; } = ValidPrefix(NumberPrefix);

    /// <summary>The queue permission key.</summary>
    public string QueuePermissionKey { get; } = ValidPermission(QueuePermissionKey, nameof(QueuePermissionKey));

    /// <summary>The view-own permission key.</summary>
    public string ViewOwnPermissionKey { get; } = ValidPermission(ViewOwnPermissionKey, nameof(ViewOwnPermissionKey));

    /// <summary>The attachment record type.</summary>
    public string AttachableRecordType { get; } = RecordTypeKeyFormat.EnsureValid(AttachableRecordType, nameof(AttachableRecordType));

    private static string ValidPrefix(string prefix)
    {
        RequestNumberFormat.EnsureValidPrefix(prefix);
        return prefix;
    }

    private static string ValidPermission(string key, string parameterName) =>
        PermissionKeyFormat.IsValid(key)
            ? key
            : throw new ArgumentException($"A permission key matches {PermissionKeyFormat.Pattern} (at most {PermissionKeyFormat.MaxLength} characters).", parameterName);
}

/// <summary>
/// The record types one module owns. The owning module registers one source; the Records module reads every source at
/// startup (zero or more), so a record type exists exactly when its module is in the host.
/// </summary>
public interface IRecordTypeRegistrationSource
{
    /// <summary>The module's record types; stable for the process lifetime.</summary>
    IEnumerable<RecordTypeRegistration> Registrations { get; }
}
