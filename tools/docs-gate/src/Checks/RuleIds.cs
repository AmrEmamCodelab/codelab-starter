namespace Starter.Tools.DocsGate.Checks;

/// <summary>Stable rule ids printed with every finding; docs and CI logs refer to them.</summary>
internal static class RuleIds
{
    public const string ModuleDocMissing = "DOCS001";
    public const string ModuleDocSectionMissing = "DOCS002";
    public const string DeadLink = "DOCS003";
    public const string MergeMarker = "DOCS004";
    public const string ModuleChangedWithoutDocs = "DOCS005";
}
