namespace Starter.Domain.Workflow;

/// <summary>
/// Checks a <see cref="TransitionCatalog"/> before the engine enforces it and reports EVERY problem at once (the host
/// lists them all when it refuses to start). Pure: no I/O, no exceptions for a bad catalog.
/// </summary>
public static class TransitionCatalogValidator
{
    private const string CatalogSubject = "catalog";

    /// <summary>Every violation of <paramref name="catalog"/>; empty when it is safe to enforce.</summary>
    /// <param name="catalog">The catalog to check.</param>
    /// <param name="knownPermissionKeys">Every key of the permission catalog (the union of the registered sources).</param>
    public static IReadOnlyList<CatalogViolation> Validate(TransitionCatalog catalog, IReadOnlySet<string> knownPermissionKeys)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(knownPermissionKeys);
        var report = new ViolationReport(catalog.RecordType);
        CheckStatuses(catalog, report);
        CheckInitialState(catalog, report);
        foreach (var rule in catalog.Rules)
        {
            CheckRuleStates(catalog, rule, report);
            CheckPermission(rule, knownPermissionKeys, report);
            CheckReasonAndLabel(rule, report);
            CheckNotification(rule, report);
        }

        CheckDuplicateActions(catalog, report);
        CheckReachability(catalog, report);
        return report.Violations;
    }

    private static void CheckStatuses(TransitionCatalog catalog, ViolationReport report)
    {
        var seen = new HashSet<int>();
        foreach (var status in catalog.Statuses)
        {
            var subject = StatusSubject(status);
            if (!seen.Add(status.Status))
            {
                report.Add(CatalogViolationKind.UndefinedState, subject, $"status value {status.Status} is defined more than once; give every status its own value.");
            }

            if (string.IsNullOrWhiteSpace(status.Key))
            {
                report.Add(CatalogViolationKind.MissingWording, subject, "the status has no key; its i18n keys and wire key derive from it.");
            }

            CheckWording(status, subject, report);
        }
    }

    private static void CheckWording(WorkflowStatusDefinition status, string subject, ViolationReport report)
    {
        foreach (var audience in Enum.GetValues<WorkflowAudience>())
        {
            if (status.Wording is null || string.IsNullOrWhiteSpace(status.Wording.For(audience)))
            {
                report.Add(CatalogViolationKind.MissingWording, subject, $"no {audience} wording key; every status is worded for every audience.");
            }
        }
    }

    private static void CheckInitialState(TransitionCatalog catalog, ViolationReport report)
    {
        var initial = catalog.Statuses.Count(status => status.IsInitial);
        if (initial != 1)
        {
            report.Add(CatalogViolationKind.NoInitialState, CatalogSubject, $"exactly one status must be initial; found {initial}.");
        }
    }

    private static void CheckRuleStates(TransitionCatalog catalog, TransitionRule rule, ViolationReport report)
    {
        var subject = rule.Describe();
        if (catalog.FindStatus(rule.FromStatus) is null)
        {
            report.Add(CatalogViolationKind.UndefinedState, subject, $"from-status {rule.FromStatus} is not a status of the catalog.");
        }

        if (catalog.FindStatus(rule.ToStatus) is null)
        {
            report.Add(CatalogViolationKind.UndefinedState, subject, $"to-status {rule.ToStatus} is not a status of the catalog.");
        }

        if (catalog.IsTerminal(rule.FromStatus))
        {
            report.Add(CatalogViolationKind.TransitionFromTerminal, subject, $"status {rule.FromStatus} is terminal; nothing may leave it.");
        }
    }

    private static void CheckPermission(TransitionRule rule, IReadOnlySet<string> knownPermissionKeys, ViolationReport report)
    {
        if (string.IsNullOrWhiteSpace(rule.PermissionKey))
        {
            report.Add(CatalogViolationKind.MissingPermission, rule.Describe(), "the transition has no permission key; every transition is guarded by one.");
            return;
        }

        if (!knownPermissionKeys.Contains(rule.PermissionKey))
        {
            report.Add(CatalogViolationKind.UnknownPermission, rule.Describe(), $"permission '{rule.PermissionKey}' is not in the permission catalog; declare it in the module's permission catalog source.");
        }
    }

    private static void CheckReasonAndLabel(TransitionRule rule, ViolationReport report)
    {
        if (rule.Reason == ReasonRequirement.Required && string.IsNullOrWhiteSpace(rule.ReasonFieldKey))
        {
            report.Add(CatalogViolationKind.ReasonFieldMissing, rule.Describe(), "the reason is required but no reason field key is set, so no screen can ask for it.");
        }

        if (string.IsNullOrWhiteSpace(rule.LabelKey))
        {
            report.Add(CatalogViolationKind.MissingWording, rule.Describe(), "the action has no label key.");
        }
    }

    private static void CheckNotification(TransitionRule rule, ViolationReport report)
    {
        var decision = rule.Notification;
        if (decision is null)
        {
            report.Add(CatalogViolationKind.MissingNotificationDecision, rule.Describe(), "no notification decision; use NotificationDecision.None to notify nobody on purpose.");
            return;
        }

        var hasTemplate = !string.IsNullOrWhiteSpace(decision.TemplateKey);
        if (hasTemplate != decision.NotifiesAnyone)
        {
            var message = hasTemplate
                ? $"template '{decision.TemplateKey}' is named but nobody is notified."
                : "recipients are notified but no template key is named.";
            report.Add(CatalogViolationKind.MissingNotificationDecision, rule.Describe(), message);
        }
    }

    private static void CheckDuplicateActions(TransitionCatalog catalog, ViolationReport report)
    {
        var duplicates = catalog.Rules
            .GroupBy(rule => (rule.ActionKey, rule.FromStatus))
            .Where(group => group.Count() > 1)
            .Select(group => group.First());
        foreach (var rule in duplicates)
        {
            report.Add(CatalogViolationKind.DuplicateAction, rule.Describe(), $"action '{rule.ActionKey}' is declared more than once from status {rule.FromStatus}.");
        }
    }

    private static void CheckReachability(TransitionCatalog catalog, ViolationReport report)
    {
        var reached = catalog.Statuses.Where(status => status.IsInitial).Select(status => status.Status).ToHashSet();
        if (reached.Count == 0)
        {
            return;
        }

        var pending = new Queue<int>(reached);
        while (pending.TryDequeue(out var current))
        {
            foreach (var rule in catalog.RulesFrom(current))
            {
                if (reached.Add(rule.ToStatus))
                {
                    pending.Enqueue(rule.ToStatus);
                }
            }
        }

        foreach (var status in catalog.Statuses.Where(status => !reached.Contains(status.Status)))
        {
            report.Add(CatalogViolationKind.UnreachableState, StatusSubject(status), "no chain of transitions leads here from the initial status.");
        }
    }

    private static string StatusSubject(WorkflowStatusDefinition status) => $"status {status.Key} ({status.Status})";

    private sealed class ViolationReport(string recordType)
    {
        private readonly List<CatalogViolation> _violations = [];

        public IReadOnlyList<CatalogViolation> Violations => _violations;

        public void Add(CatalogViolationKind kind, string subject, string message) =>
            _violations.Add(new CatalogViolation(recordType, kind, subject, message));
    }
}
