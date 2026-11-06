// The C# new-transition writes: the status and rule entries of the catalog lists, the constants of the statuses,
// status keys, actions and notification keys classes, and the test stubs' bodies. Pure: refs in, lines out. Entries
// use named arguments throughout, so they compile next to any helper style the workflow class uses.
import { AUDIENCES, AUDIENCE_MEMBERS, I18N_ACTION_SEGMENT, I18N_SEPARATOR, I18N_STATUS_SEGMENT, Reasons, Sides } from './constants.mjs';
import { csharpString } from './csharp-source.mjs';

/** The i18n key of a status for an audience: <prefix>.status.<key>.<audience>. */
export function wordingKey(prefix, statusKey, audience) {
  return [prefix, I18N_STATUS_SEGMENT, statusKey, audience].join(I18N_SEPARATOR);
}

/** The i18n key of an action label: <prefix>.action.<key>. */
export function labelKey(prefix, actionKey) {
  return [prefix, I18N_ACTION_SEGMENT, actionKey].join(I18N_SEPARATOR);
}

/**
 * The status entry of the Statuses list.
 * @param {{ statusRef: string, keyRef: string, terminal: boolean, prefix: string, statusKey: string, wordingHelper: boolean }} refs
 */
export function statusEntry(refs) {
  const wording = AUDIENCES.map((audience, index) => {
    const name = `${AUDIENCE_MEMBERS[index]}Key`;
    const value = refs.wordingHelper
      ? `WordingKey(${refs.keyRef}, WorkflowAudience.${AUDIENCE_MEMBERS[index]})`
      : csharpString(wordingKey(refs.prefix, refs.statusKey, audience));
    return `${name}: ${value}`;
  });
  return [
    'new WorkflowStatusDefinition(',
    `    Status: ${refs.statusRef},`,
    `    Key: ${refs.keyRef},`,
    '    IsInitial: false,',
    `    IsTerminal: ${refs.terminal},`,
    '    Wording: new StatusWording(',
    `        ${wording[0]},`,
    `        ${wording[1]},`,
    `        ${wording[2]})),`,
  ];
}

/** The NotificationDecision expression of a rule. */
export function notificationExpression(notification, templateRef) {
  if (notification.none) return 'NotificationDecision.None';
  return `new NotificationDecision(TemplateKey: ${templateRef}, NotifyRequester: ${notification.requester}, NotifyAssignee: ${notification.assignee})`;
}

/**
 * One rule entry of the Rules list.
 * @param {{ actionRef: string, fromRef: string, toRef: string, permissionRef: string, side: string, reason: string,
 *   reasonFieldRef: string, labelRef: string, notification: string, owner: boolean }} refs
 */
export function ruleEntry(refs) {
  return [
    'new TransitionRule(',
    `    ActionKey: ${refs.actionRef},`,
    `    FromStatus: ${refs.fromRef},`,
    `    ToStatus: ${refs.toRef},`,
    `    PermissionKey: ${refs.permissionRef},`,
    `    Side: TransitionSide.${Sides[refs.side]},`,
    `    Reason: ReasonRequirement.${Reasons[refs.reason]},`,
    `    ReasonFieldKey: ${refs.reasonFieldRef},`,
    `    LabelKey: ${refs.labelRef},`,
    `    Notification: ${refs.notification},`,
    `    RequiresOwnership: ${refs.owner}),`,
  ];
}

/** An int constant with its summary (`summaryXml` is XML doc text: escape user text with xmlText). */
export function intConstant(name, value, summaryXml) {
  return [`/// <summary>${summaryXml}</summary>`, `public const int ${name} = ${value};`];
}

/** A string constant with its summary (`summaryXml` is XML doc text: escape user text with xmlText). */
export function stringConstant(name, value, summaryXml) {
  return [`/// <summary>${summaryXml}</summary>`, `public const string ${name} = ${csharpString(value)};`];
}

/** "requester side, owner only, reason required" — the action constant's summary tail. */
export function ruleSummary(rule, fromKeys) {
  const parts = [`${rule.side} side`];
  if (rule.owner) parts.push('owner only');
  if (rule.reason !== 'none') parts.push(`reason ${rule.reason}`);
  return `${fromKeys.join(' or ')} to ${rule.toKey} (${parts.join(', ')}).`;
}
