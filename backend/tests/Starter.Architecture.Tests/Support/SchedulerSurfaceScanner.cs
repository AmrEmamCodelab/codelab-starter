using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Starter.Architecture.Tests.Support;

/// <summary>
/// Rules <c>base.no-scheduler-dashboard-mapping</c> and <c>base.no-static-scheduler-storage</c> over every backend file, not only
/// the Jobs module: the scheduler dashboard is never mapped (at any path) and the scheduler storage never comes from
/// static state. Parsed with Roslyn, so a mention in a comment or a string is not a finding.
/// </summary>
public static class SchedulerSurfaceScanner
{
    // Built from parts so the repository content rules, which match the whole names, do not flag this file.
    private const string Scheduler = "Hangfire";
    private const string Dashboard = Scheduler + "Dashboard";
    private const string StorageClass = "JobStorage";
    private const string StaticMember = "Current";
    private const char NamespaceSeparator = '.';

    /// <summary>The members that map the dashboard or configure static storage.</summary>
    public static IReadOnlyList<string> BannedMembers { get; } = ["Use" + Dashboard, "Map" + Dashboard, "UseSqlServer" + "Storage"];

    /// <summary>Every banned use in <paramref name="source"/>, as <c>file:line member</c>.</summary>
    public static IReadOnlyList<string> Scan(OwnedSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var root = CSharpSyntaxTree.ParseText(source.Text).GetRoot();
        var members = root.DescendantNodes().OfType<SimpleNameSyntax>()
            .Where(name => BannedMembers.Contains(name.Identifier.Text, StringComparer.Ordinal))
            .Select(name => Describe(source, name, name.Identifier.Text));
        var staticStorage = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(IsStaticStorage)
            .Select(access => Describe(source, access, access.ToString()));
        return [.. members.Concat(staticStorage)];
    }

    private static bool IsStaticStorage(MemberAccessExpressionSyntax access) =>
        string.Equals(access.Name.Identifier.Text, StaticMember, StringComparison.Ordinal)
        && access.Expression is SimpleNameSyntax or MemberAccessExpressionSyntax
        && access.Expression.ToString().Split(NamespaceSeparator)[^1] is StorageClass;

    private static string Describe(OwnedSource source, Microsoft.CodeAnalysis.SyntaxNode node, string what) =>
        $"{source.Name}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1} {what}";
}
