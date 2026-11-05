using Starter.DocsGen.Collection;

namespace Starter.DocsGen.Rendering;

/// <summary>Renders <c>config-keys.md</c>: every configuration key, grouped by owner, then options section.</summary>
internal static class ConfigKeysRenderer
{
    private const string Title = "Configuration keys";
    private const string Intro =
        "Every key bound by an options class that declares `public const string SectionName`, with the default a fresh " +
        "options instance carries. `<n>` is an array index and `<name>` a dictionary key. Secrets (keys, passwords, " +
        "connection strings) never go into a committed file: Development reads them from " +
        "`appsettings.Development.local.json` (`tools/devseed/New-LocalSecrets.ps1`), servers from the environment.";

    private const string NoKeys = "No options class declares a section.";
    private const int OwnerHeadingLevel = 2;
    private const int SectionHeadingLevel = 3;

    private static readonly string[] Headers = ["Key", "Type", "Default", "Description"];

    /// <summary>One section per owner and options section (ordinal order), each a table sorted by key.</summary>
    public static string Render(IReadOnlyList<ConfigKeyDoc> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var document = MarkdownText.Document(Title).Line(Intro).Line();
        if (keys.Count == 0)
        {
            return document.Line(NoKeys).ToString();
        }

        foreach (var owner in keys.GroupBy(key => key.Owner, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            document.Heading(OwnerHeadingLevel, owner.Key);
            foreach (var section in owner.GroupBy(key => key.Section, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var rows = section
                    .OrderBy(key => key.Key, StringComparer.Ordinal)
                    .Select(key => (IReadOnlyList<string>)
                        [MarkdownText.Code(key.Key), key.Type, MarkdownText.Code(key.DefaultValue), key.Description]);
                document.Heading(SectionHeadingLevel, MarkdownText.Code(section.Key)).Table(Headers, rows, NoKeys);
            }
        }

        return document.ToString();
    }
}
