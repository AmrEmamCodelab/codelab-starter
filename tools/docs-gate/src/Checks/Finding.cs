namespace Starter.Tools.DocsGate.Checks;

/// <summary>One rule violation.</summary>
/// <param name="RuleId">One of <see cref="RuleIds"/>.</param>
/// <param name="Path">Root-relative path with '/' separators.</param>
/// <param name="Line">1-based line, or <see cref="NoLine"/> when the finding is about a whole file or folder.</param>
/// <param name="Message">What is wrong and how to fix it.</param>
internal sealed record Finding(string RuleId, string Path, int Line, string Message)
{
    public const int NoLine = 0;

    public override string ToString() =>
        Line == NoLine
            ? $"{RuleId} {Path}: {Message}"
            : FormattableString.Invariant($"{RuleId} {Path}:{Line}: {Message}");
}
