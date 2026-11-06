namespace Starter.Modules.Attachments.Domain;

/// <summary>
/// One attachment type of the catalog (<c>attachments.ServiceAttachments</c>): which record type it belongs to, whether
/// a record needs one before it can move on (<see cref="IsRequired"/>), how many files a record may hold under it and
/// which extensions it accepts (a subset of <c>Codelab:Security:Uploads:AllowedExtensions</c>, checked at start).
/// Created only by a migration, with a fixed <see cref="Id"/> from <c>Starter.Domain.Attachments.AttachmentTypeIds</c>;
/// at run time only <see cref="IsActive"/> changes.
/// </summary>
public sealed class ServiceAttachment
{
    private ServiceAttachment()
    {
    }

    /// <summary>The fixed id (an <c>AttachmentTypeIds</c> constant; never generated).</summary>
    public int Id { get; private set; }

    /// <summary>The kebab-case key, unique (an <c>AttachmentTypeKeys</c> constant).</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>The Arabic display name.</summary>
    public string NameAr { get; private set; } = string.Empty;

    /// <summary>The English display name.</summary>
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>The record type whose records carry this type (an <c>AttachableRecordTypes</c> constant).</summary>
    public string RecordType { get; private set; } = string.Empty;

    /// <summary>True when a record needs at least one live attachment of this type (the interlock).</summary>
    public bool IsRequired { get; private set; }

    /// <summary>The most live attachments one record may hold under this type (1 to <see cref="AttachmentsLimits.MaxFilesCeiling"/>).</summary>
    public int MaxFiles { get; private set; }

    /// <summary>False when the type is retired: no new uploads, not required; existing attachments stay readable.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>The allowed extensions, lower-case with the dot (<c>.pdf</c>), in seed order.</summary>
    public IReadOnlyList<string> AllowedExtensions => AttachmentExtensions.FromStorage(AllowedExtensionsValue);

    /// <summary>The stored form of <see cref="AllowedExtensions"/> (<see cref="AttachmentExtensions.ToStorage"/>); mapped by the EF configuration only.</summary>
    internal string AllowedExtensionsValue { get; private set; } = string.Empty;

    /// <summary>A type as a migration seeds it (tests build the same rows).</summary>
    /// <exception cref="ArgumentException">A key, name, record type, file count or extension is not well formed.</exception>
    public static ServiceAttachment Seeded(ServiceAttachmentSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        if (!IsWellFormed(seed))
        {
            throw new ArgumentException("The attachment type seed has a key, name, record type or file count that is not well formed.", nameof(seed));
        }

        return new ServiceAttachment
        {
            Id = seed.Id,
            Key = seed.Key,
            NameAr = seed.Names.Ar,
            NameEn = seed.Names.En,
            RecordType = seed.RecordType,
            IsRequired = seed.Rules.IsRequired,
            MaxFiles = seed.Rules.MaxFiles,
            AllowedExtensionsValue = AttachmentExtensions.ToStorage(seed.Rules.AllowedExtensions),
            IsActive = seed.IsActive,
        };
    }

    /// <summary>True when this type accepts <paramref name="fileName"/> by its (normalised) extension.</summary>
    public bool AllowsFile(string fileName)
    {
        var extension = AttachmentExtensions.Of(fileName);
        return extension.Length > 0 && AllowedExtensions.Contains(extension, StringComparer.Ordinal);
    }

    /// <summary>True when a record that already holds <paramref name="liveCount"/> live attachments of this type may take one more.</summary>
    public bool HasRoomFor(int liveCount) => liveCount < MaxFiles;

    /// <summary>How many more files a record holding <paramref name="liveCount"/> live attachments of this type may take (never negative).</summary>
    public int RemainingSlots(int liveCount) => Math.Max(0, MaxFiles - liveCount);

    /// <summary>True when this type counts for the interlock: active and required.</summary>
    public bool IsEnforcedAsRequired => IsActive && IsRequired;

    /// <summary>Switches the type on or off; true when the state changed.</summary>
    public bool SetActive(bool isActive)
    {
        if (IsActive == isActive)
        {
            return false;
        }

        IsActive = isActive;
        return true;
    }

    private static bool IsWellFormed(ServiceAttachmentSeed seed) =>
        seed.Id >= 1
        && AttachmentsLimits.IsTypeKey(seed.Key)
        && AttachmentsLimits.IsRecordType(seed.RecordType)
        && IsName(seed.Names.Ar)
        && IsName(seed.Names.En)
        && seed.Rules.MaxFiles is >= 1 and <= AttachmentsLimits.MaxFilesCeiling;

    private static bool IsName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= AttachmentsLimits.NameMaxLength;
}

/// <summary>The seeded content of one attachment type.</summary>
/// <param name="Id">The fixed id (an <c>AttachmentTypeIds</c> constant).</param>
/// <param name="Key">The kebab-case key (an <c>AttachmentTypeKeys</c> constant).</param>
/// <param name="Names">The Arabic and English names.</param>
/// <param name="RecordType">The record type (an <c>AttachableRecordTypes</c> constant).</param>
/// <param name="Rules">Required flag, file count and extensions.</param>
/// <param name="IsActive">Whether the type is offered (seeded types are active).</param>
public sealed record ServiceAttachmentSeed(int Id, string Key, AttachmentTypeNames Names, string RecordType, AttachmentTypeRules Rules, bool IsActive = true);

/// <summary>An Arabic and an English name (one pair, so the two can never be swapped at a call site).</summary>
/// <param name="Ar">The Arabic name.</param>
/// <param name="En">The English name.</param>
public sealed record AttachmentTypeNames(string Ar, string En);

/// <summary>The rules of one attachment type.</summary>
/// <param name="IsRequired">True when a record needs at least one attachment of the type.</param>
/// <param name="MaxFiles">The most live attachments per record (1 to <see cref="AttachmentsLimits.MaxFilesCeiling"/>).</param>
/// <param name="AllowedExtensions">The accepted extensions (<c>.pdf</c> or <c>pdf</c>; normalised on seed).</param>
public sealed record AttachmentTypeRules(bool IsRequired, int MaxFiles, IReadOnlyList<string> AllowedExtensions);
