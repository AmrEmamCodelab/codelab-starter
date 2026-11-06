namespace Starter.Modules.Records.Application;

/// <summary>
/// The action keys the Records lifecycle gives a meaning to. A record type's workflow catalog names its submit, resubmit
/// and cancel rules with exactly these keys (the generic transitions endpoint routes <see cref="Submit"/> and
/// <see cref="Resubmit"/> through the submit checks); every other action key is the record type's own.
/// </summary>
public static class RecordLifecycleActions
{
    /// <summary>The first timeline entry of a record (history only; no catalog rule has this key).</summary>
    public const string Create = "create";

    /// <summary>The first submit: runs the submit validators and the required-attachments gate, then takes the number.</summary>
    public const string Submit = "submit";

    /// <summary>A submit after a return: the same checks, and the record keeps its number.</summary>
    public const string Resubmit = "resubmit";

    /// <summary>The requester withdraws the record (<c>IRecordLifecycle.CancelAsync</c>).</summary>
    public const string Cancel = "cancel";

    /// <summary>True for the two actions that pass the submit checks.</summary>
    public static bool IsSubmit(string? actionKey) =>
        string.Equals(actionKey, Submit, StringComparison.Ordinal) || string.Equals(actionKey, Resubmit, StringComparison.Ordinal);
}
