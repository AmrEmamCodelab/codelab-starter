namespace Starter.Tools.DocsGate.Cli;

/// <summary>Options of the <c>check</c> command.</summary>
/// <param name="Root">Repository (or fixture) root to check; resolved to a full path.</param>
/// <param name="BaseRef">
/// Git ref to diff against for the module-changed check (the merge base with HEAD is used).
/// Null means "working tree changes since HEAD, plus untracked files".
/// </param>
internal sealed record CheckOptions(string Root, string? BaseRef);
