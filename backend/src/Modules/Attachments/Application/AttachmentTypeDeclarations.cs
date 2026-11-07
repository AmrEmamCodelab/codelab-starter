using Starter.Application.Attachments;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Application;

/// <summary>
/// The attachment types owning modules declare (every registered <see cref="IAttachmentTypeSeedSource"/>), validated
/// once when built: each definition must be well formed (<see cref="ServiceAttachment.Seeded"/>), ids and keys must be
/// unique across every source, and no declaration may take the id or key of a type the Attachments module seeds by its
/// own migrations (the reserved types). A violation is a configuration fault that stops the host at start.
/// </summary>
public sealed class AttachmentTypeDeclarations
{
    /// <summary>Collects and validates the declarations of <paramref name="sources"/> against <paramref name="reserved"/>.</summary>
    /// <param name="sources">The registered sources (zero or more).</param>
    /// <param name="reserved">The types the Attachments migrations own (never declared by a module).</param>
    /// <exception cref="InvalidOperationException">A definition is malformed, duplicated or takes a reserved id or key.</exception>
    public AttachmentTypeDeclarations(IEnumerable<IAttachmentTypeSeedSource> sources, IEnumerable<ServiceAttachment> reserved)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(reserved);
        var definitions = sources.SelectMany(source => source.Types).ToList();
        var seeds = definitions.Select(ToSeed).ToList();
        EnsureUnique(seeds, [.. reserved]);
        Seeds = [.. seeds.OrderBy(seed => seed.Id)];
        Types = [.. Seeds.Select(ServiceAttachment.Seeded)];
    }

    /// <summary>Every declared type as a seed, ordered by id.</summary>
    public IReadOnlyList<ServiceAttachmentSeed> Seeds { get; }

    /// <summary>Every declared type as the catalog row it becomes, ordered by id (for the allowlist check).</summary>
    public IReadOnlyList<ServiceAttachment> Types { get; }

    /// <summary>The seed of one definition (names and rules grouped as the catalog stores them).</summary>
    /// <exception cref="InvalidOperationException">The definition is not well formed.</exception>
    public static ServiceAttachmentSeed ToSeed(AttachmentTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var seed = new ServiceAttachmentSeed(
            Id: definition.Id,
            Key: definition.Key,
            Names: new AttachmentTypeNames(Ar: definition.NameAr, En: definition.NameEn),
            RecordType: definition.RecordType,
            Rules: new AttachmentTypeRules(IsRequired: definition.IsRequired, MaxFiles: definition.MaxFiles, AllowedExtensions: definition.AllowedExtensions));
        try
        {
            _ = ServiceAttachment.Seeded(seed);
            return seed;
        }
        catch (ArgumentException malformed)
        {
            throw new InvalidOperationException($"The declared attachment type {definition.Id} '{definition.Key}' is not well formed: {malformed.Message}", malformed);
        }
    }

    private static void EnsureUnique(List<ServiceAttachmentSeed> seeds, List<ServiceAttachment> reserved)
    {
        var faults = DuplicatesOf(seeds.Select(seed => seed.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), "id")
            .Concat(DuplicatesOf(seeds.Select(seed => seed.Key), "key"))
            .Concat(seeds.Where(seed => reserved.Exists(type => type.Id == seed.Id || string.Equals(type.Key, seed.Key, StringComparison.Ordinal)))
                .Select(seed => $"{seed.Key} (id {seed.Id}) takes a type the Attachments migrations own"))
            .ToList();
        if (faults.Count > 0)
        {
            throw new InvalidOperationException("Declared attachment types collide: " + string.Join("; ", faults) + ".");
        }
    }

    private static IEnumerable<string> DuplicatesOf(IEnumerable<string> values, string what) =>
        values.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => $"{what} {group.Key} declared {group.Count()} times");
}
