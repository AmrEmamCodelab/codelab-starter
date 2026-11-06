using Starter.Domain.Workflow;

namespace Starter.Application.Workflow;

/// <summary>An action the caller may take now, computed by the server from the catalog, the caller's permissions and ownership.</summary>
/// <param name="ActionKey">The action key to send back in a transition request.</param>
/// <param name="LabelKey">The i18n key of the button label.</param>
/// <param name="Reason">Whether the dialog asks for a reason.</param>
/// <param name="ReasonFieldKey">The reason's form field, or null when the rule names none.</param>
/// <param name="ToStatus">The status the record lands on.</param>
/// <param name="ToTerminal">True when the record is closed by the action.</param>
public sealed record AllowedAction(string ActionKey, string LabelKey, ReasonRequirement Reason, string? ReasonFieldKey, int ToStatus, bool ToTerminal);
