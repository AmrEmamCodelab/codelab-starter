using System.Text.RegularExpressions;
using Starter.Architecture.Tests.Support;

namespace Starter.Architecture.Tests.Engine;

/// <summary>One file of the generic engine, with its repository-relative path.</summary>
/// <param name="Path">The path relative to the repository root, with forward slashes.</param>
/// <param name="Text">The file text.</param>
public sealed record EngineSource(string Path, string Text);

/// <summary>One vocabulary rule: an id for the finding and the pattern it denies.</summary>
/// <param name="Id">The rule id (shown in findings).</param>
/// <param name="Pattern">The denied pattern.</param>
public sealed record VocabularyRule(string Id, Regex Pattern);

/// <summary>
/// The domain-vocabulary sweep of the generic business engine (build-plan phase 7, "Fixes applied"): the Workflow and
/// Records kernel folders, the Records, Assignments and Deadlines modules, the workflow test kit and the generic
/// record UI must not name the sample record type or the harvest source's domain. Findings read
/// <c>path:line rule-id</c>.
/// </summary>
public static class EngineVocabularyScanner
{
    /// <summary>The kernel area folders that hold generic engine code inside every <c>Starter.*</c> kernel project.</summary>
    public static readonly IReadOnlyList<string> KernelAreaFolders = ["Workflow", "Records"];

    /// <summary>The generic engine modules under <c>backend/src/Modules</c>.</summary>
    public static readonly IReadOnlyList<string> EngineModules = ["Records", "Assignments", "Deadlines"];

    /// <summary>Other generic engine folders, relative to the repository root.</summary>
    public static readonly IReadOnlyList<string> OtherEngineFolders =
    [
        "backend/tests/Starter.Workflow.Testing",
        "frontend/starter-web/src/app/shared/records",
    ];

    /// <summary>The code files the sweep reads (docs may point to the sample as the worked example).</summary>
    public static readonly IReadOnlyList<string> CodeExtensions = [".cs", ".ts", ".html"];

    /// <summary>
    /// The sample's vocabulary: its type and key names in any casing or separator (ServiceRequest, service-request,
    /// serviceRequests, service_request, the category lookup key), and its number prefix as a literal or a number.
    /// </summary>
    public static readonly IReadOnlyList<VocabularyRule> SampleRules =
    [
        new("sample-record-type", Pattern(@"service[-_ ]?requests?", RegexOptions.IgnoreCase)),
        new("sample-number-prefix", Pattern(@"[""']SR[""'-]|\bSR-\d{4}\b", RegexOptions.None)),
    ];

    private const string ModulesFolder = "backend/src/Modules";
    private const string KernelProjectPattern = "Starter.*";
    private const string TermsFolder = "tools/extract";
    private const string TermsFileSuffix = "-terms.txt";
    private const char CommentMarker = '#';
    private const char FlagSeparator = '/';
    private const string RuleSeparator = ": ";
    private const string IgnoreCaseFlag = "i";
    private const string TermRulePrefix = "term-";
    private const int MatchTimeoutSeconds = 2;
    private static readonly string[] BuildOutputSegments = ["bin", "obj", "node_modules"];
    private static readonly Regex CaseBoundary = Pattern("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|_", RegexOptions.None);

    /// <summary>The engine folders that exist in the repository (absolute paths).</summary>
    public static IReadOnlyList<string> EngineFolders(string repositoryRoot)
    {
        var backendSource = Path.Combine(repositoryRoot, SourceFiles.BackendSourceFolder);
        var kernel = Directory.EnumerateDirectories(backendSource, KernelProjectPattern)
            .SelectMany(project => KernelAreaFolders.Select(area => Path.Combine(project, area)));
        var modules = EngineModules.Select(module => Path.Combine(repositoryRoot, ModulesFolder, module));
        var others = OtherEngineFolders.Select(folder => Path.Combine(repositoryRoot, folder));
        return [.. kernel.Concat(modules).Concat(others).Where(Directory.Exists).Order(StringComparer.Ordinal)];
    }

    /// <summary>Every code file under the engine folders (build output excluded).</summary>
    public static IReadOnlyList<EngineSource> ReadEngine(string repositoryRoot) =>
        [.. EngineFolders(repositoryRoot)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(path => CodeExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Where(relative => !relative.Split('/').Any(segment => BuildOutputSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .Select(relative => new EngineSource(relative, File.ReadAllText(Path.Combine(repositoryRoot, relative))))];

    /// <summary>
    /// The source-project term rules of the repository's scrub sweep (the one <c>*-terms.txt</c> denylist under
    /// <c>tools/extract</c>), so this sweep and the scrub sweep share one list. Format: <c>id[/flags]: regex</c>.
    /// </summary>
    public static IReadOnlyList<VocabularyRule> SourceTermRules(string repositoryRoot)
    {
        var termsFile = Assert.Single(Directory.EnumerateFiles(Path.Combine(repositoryRoot, TermsFolder), "*" + TermsFileSuffix));
        return ParseTermRules(File.ReadAllLines(termsFile));
    }

    /// <summary>Parses denylist lines (blank and <c>#</c> lines skipped); the <c>i</c> flag makes a rule case-insensitive.</summary>
    public static IReadOnlyList<VocabularyRule> ParseTermRules(IEnumerable<string> lines) =>
        [.. lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line[0] != CommentMarker)
            .Select(ParseTermRule)];

    /// <summary>
    /// Every match of <paramref name="rules"/> in <paramref name="sources"/>, on each line as written and on a copy with
    /// identifiers split at case and underscore boundaries (so <c>FooBarId</c> is also read as <c>Foo Bar Id</c>).
    /// </summary>
    public static IReadOnlyList<string> Scan(IEnumerable<EngineSource> sources, IReadOnlyList<VocabularyRule> rules) =>
        [.. sources.SelectMany(source => ScanSource(source, rules))];

    private static IEnumerable<string> ScanSource(EngineSource source, IReadOnlyList<VocabularyRule> rules)
    {
        var lines = source.Text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var split = CaseBoundary.Replace(line, " ");
            foreach (var rule in rules.Where(rule => rule.Pattern.IsMatch(line) || rule.Pattern.IsMatch(split)))
            {
                yield return $"{source.Path}:{index + 1} {rule.Id}";
            }
        }
    }

    private static VocabularyRule ParseTermRule(string line)
    {
        var separator = line.IndexOf(RuleSeparator, StringComparison.Ordinal);
        Assert.True(separator > 0, $"A term rule reads 'id[/flags]: regex': {line}");
        var head = line[..separator];
        var flagStart = head.IndexOf(FlagSeparator, StringComparison.Ordinal);
        var id = flagStart < 0 ? head : head[..flagStart];
        var flags = flagStart < 0 ? string.Empty : head[(flagStart + 1)..];
        var options = flags.Contains(IgnoreCaseFlag, StringComparison.Ordinal) ? RegexOptions.IgnoreCase : RegexOptions.None;
        return new VocabularyRule(TermRulePrefix + id, Pattern(line[(separator + RuleSeparator.Length)..].Trim(), options));
    }

    private static Regex Pattern(string pattern, RegexOptions options) =>
        new(pattern, options | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(MatchTimeoutSeconds));
}
