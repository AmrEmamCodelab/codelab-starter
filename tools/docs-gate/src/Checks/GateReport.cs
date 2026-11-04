namespace Starter.Tools.DocsGate.Checks;

/// <summary>Result of one docs-gate run: findings fail the gate, notices are informational.</summary>
internal sealed record GateReport(IReadOnlyList<Finding> Findings, IReadOnlyList<string> Notices);
