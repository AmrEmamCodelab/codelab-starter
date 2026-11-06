using System.Reflection;

namespace Starter.DocsGen.Collection;

/// <summary>
/// Reads the status enums by reflection: every public <see cref="int"/> enum whose name ends with
/// <see cref="StatusSuffix"/> (a job-run state, an outbox message state, a request status), with its members. Status
/// enums are stored and sent as their integer values (coding standard 13), so the values are the contract this
/// document shows. Lookup ids seeded by migrations are not enums and are not listed.
/// </summary>
internal static class StatusScanner
{
    /// <summary>The suffix of every documented enum (<c>JobRunStatus</c>, <c>OutboxMessageStatus</c>).</summary>
    public const string StatusSuffix = "Status";

    /// <summary>The status enums of <paramref name="assemblies"/>, sorted by owner, then name, then full name.</summary>
    public static IReadOnlyList<StatusDoc> Scan(IEnumerable<Assembly> assemblies, OwnerNaming owners, XmlDocumentation documentation)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(documentation);
        return [.. assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(IsStatusEnum)
            .Select(type => new StatusDoc(
                Name: type.Name,
                FullName: type.FullName ?? type.Name,
                Owner: owners.OwnerOf(type),
                Description: documentation.SummaryOf(type),
                Members: MembersOf(type, documentation)))
            .OrderBy(status => status.Owner, StringComparer.Ordinal)
            .ThenBy(status => status.Name, StringComparer.Ordinal)
            .ThenBy(status => status.FullName, StringComparer.Ordinal)];
    }

    /// <summary>True for an <see cref="int"/> enum whose name ends with <see cref="StatusSuffix"/>.</summary>
    public static bool IsStatusEnum(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsEnum
            && Enum.GetUnderlyingType(type) == typeof(int)
            && type.Name.EndsWith(StatusSuffix, StringComparison.Ordinal);
    }

    private static IReadOnlyList<StatusMemberDoc> MembersOf(Type type, XmlDocumentation documentation) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => new StatusMemberDoc(
                Value: (int)field.GetRawConstantValue()!,
                Name: field.Name,
                Description: documentation.SummaryOf(field)))
            .OrderBy(member => member.Value)
            .ThenBy(member => member.Name, StringComparer.Ordinal)];
}
