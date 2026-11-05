using System.IO.Compression;
using System.Xml;

namespace Starter.Modules.Users.Infrastructure.Import;

/// <summary>
/// The parts of an .xlsx package (an OPC zip) the import needs: the workbook, its first worksheet and the shared
/// strings, found through the relationship parts as the format defines them. Every part is read through the shared
/// <see cref="ReadBudget"/> with a hardened <see cref="XmlReader"/> (no DTD, no external resolution).
/// </summary>
public sealed class XlsxPackage(ZipArchive archive, ReadBudget budget)
{
    private const string PackageRelationships = "_rels/.rels";
    private const string DefaultWorkbookPath = "xl/workbook.xml";
    private const string RelationshipsFolder = "_rels/";
    private const string RelationshipsExtension = ".rels";
    private const string OfficeDocumentType = "/officeDocument";
    private const string WorksheetType = "/worksheet";
    private const string SharedStringsType = "/sharedStrings";
    private const string RelationshipElement = "Relationship";
    private const string SheetElement = "sheet";
    private const string IdAttribute = "Id";
    private const string TypeAttribute = "Type";
    private const string TargetAttribute = "Target";
    private const string RelationshipIdAttribute = "id";
    private const char PathSeparator = '/';
    private const string ParentSegment = "..";

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = true,
    };

    /// <summary>The part at <paramref name="path"/> as XML, or null when the package has no such part.</summary>
    public XmlReader? Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var entry = archive.GetEntry(path);
        return entry is null ? null : XmlReader.Create(new BudgetedStream(entry.Open(), budget), ReaderSettings);
    }

    /// <summary>The path of the workbook's first worksheet, or null when the package names none.</summary>
    public string? FirstSheetPath()
    {
        var workbook = Target(PackageRelationships, string.Empty, OfficeDocumentType) ?? DefaultWorkbookPath;
        var relationships = RelationshipsOf(workbook);
        var firstSheetId = FirstSheetRelationshipId(workbook);
        if (firstSheetId is null)
        {
            return null;
        }

        return ReadRelationships(relationships)
            .Where(relationship => relationship.Id == firstSheetId && relationship.Type.EndsWith(WorksheetType, StringComparison.Ordinal))
            .Select(relationship => Resolve(FolderOf(workbook), relationship.Target))
            .FirstOrDefault();
    }

    /// <summary>The path of the shared strings part, or null when the workbook has none (inline strings only).</summary>
    public string? SharedStringsPath()
    {
        var workbook = Target(PackageRelationships, string.Empty, OfficeDocumentType) ?? DefaultWorkbookPath;
        return Target(RelationshipsOf(workbook), FolderOf(workbook), SharedStringsType);
    }

    private string? Target(string relationshipsPath, string sourceFolder, string typeSuffix) =>
        ReadRelationships(relationshipsPath)
            .Where(relationship => relationship.Type.EndsWith(typeSuffix, StringComparison.Ordinal))
            .Select(relationship => Resolve(sourceFolder, relationship.Target))
            .FirstOrDefault();

    private List<PackageRelationship> ReadRelationships(string path)
    {
        var relationships = new List<PackageRelationship>();
        using var reader = Open(path);
        while (reader is not null && reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == RelationshipElement)
            {
                relationships.Add(new PackageRelationship(
                    reader.GetAttribute(IdAttribute) ?? string.Empty,
                    reader.GetAttribute(TypeAttribute) ?? string.Empty,
                    reader.GetAttribute(TargetAttribute) ?? string.Empty));
            }
        }

        return relationships;
    }

    private string? FirstSheetRelationshipId(string workbookPath)
    {
        using var reader = Open(workbookPath);
        while (reader is not null && reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == SheetElement)
            {
                return RelationshipIdOf(reader);
            }
        }

        return null;
    }

    private static string? RelationshipIdOf(XmlReader reader)
    {
        for (var index = 0; index < reader.AttributeCount; index++)
        {
            reader.MoveToAttribute(index);
            if (reader.LocalName == RelationshipIdAttribute && reader.NamespaceURI.Length > 0)
            {
                return reader.Value;
            }
        }

        return null;
    }

    private static string RelationshipsOf(string partPath)
    {
        var folder = FolderOf(partPath);
        return folder + RelationshipsFolder + partPath[folder.Length..] + RelationshipsExtension;
    }

    private static string FolderOf(string partPath)
    {
        var slash = partPath.LastIndexOf(PathSeparator);
        return slash < 0 ? string.Empty : partPath[..(slash + 1)];
    }

    /// <summary>A relationship target as a package path: absolute (<c>/xl/...</c>) or relative to the source folder.</summary>
    private static string Resolve(string sourceFolder, string target)
    {
        var combined = target.StartsWith(PathSeparator) ? target.TrimStart(PathSeparator) : sourceFolder + target;
        var segments = new List<string>();
        foreach (var segment in combined.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ParentSegment)
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
            }
            else
            {
                segments.Add(segment);
            }
        }

        return string.Join(PathSeparator, segments);
    }

    private sealed record PackageRelationship(string Id, string Type, string Target);
}
