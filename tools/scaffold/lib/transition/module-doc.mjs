// The MODULE.md note new-transition adds: one bullet per status or transition it wrote, under the
// `<!-- scaffold:workflow -->` marker when the file has one, else at the end of its "Use" section. Pure; idempotent.
import { addUseNotes } from '../module-docs.mjs';
import { WORKFLOW_RULE_ENFORCED_BY, WORKFLOW_RULE_SUFFIX, WorkflowMarkers } from './constants.mjs';
import { labelKey, wordingKey } from './entries.mjs';

const SCAFFOLD = 'added with tools/scaffold/new-transition.mjs';

function notificationText(notification) {
  if (notification.none) return 'notifies nobody';
  const recipients = [notification.requester ? 'requester' : null, notification.assignee ? 'assignee' : null].filter(Boolean);
  return `notifies ${recipients.join(' and ')} with \`${notification.templateKey}\``;
}

/** The bullets describing what the request added. */
export function moduleDocLines(request, status, rule, testFiles) {
  const lines = [];
  const tests = testFiles.map((file) => `\`${file.split('/').pop()}\``).join(', ');
  if (status) {
    const terminal = request.status.terminal ? ', terminal' : '';
    lines.push(`- Workflow status \`${request.status.key}\` (${status.value}${terminal}), ${SCAFFOLD}: wording keys \`${wordingKey(request.i18nPrefix, request.status.key, '<requester|staff|admin>')}\`.`);
  }
  if (rule && rule.added.length > 0) {
    const { rule: asked } = request;
    const reason = asked.reason === 'none' ? 'no reason' : `reason ${asked.reason} in \`${asked.reasonField}\``;
    const owner = asked.owner ? ', owner only' : '';
    lines.push(`- Workflow transition \`${asked.actionKey}\`: ${rule.added.map((from) => `\`${from.fromKey}\``).join(' or ')} to \`${asked.toKey}\` (${asked.side}${owner}, \`${asked.permissionKey}\`, ${reason}, ${notificationText(asked.notification)}; label \`${labelKey(request.i18nPrefix, asked.actionKey)}\`), ${SCAFFOLD}.`);
  }
  if (lines.length > 0 && tests.length > 0) lines.push(`  Tests: ${tests} (TransitionFixture).`);
  return lines;
}

/** Inserts `lines` (each once) into the MODULE.md text: before the workflow marker, else at the end of "Use". */
export function addModuleDocNotes(doc, lines) {
  return addUseNotes(doc, lines, WorkflowMarkers.moduleDoc);
}

/**
 * The required rule a module with a workflow carries: the flow lives only in its catalog class. The id prefix is the
 * lower-cased module name (the rules engine's module prefix).
 */
export function workflowRule(module, definitionClass) {
  return {
    id: `${module.toLowerCase()}.${WORKFLOW_RULE_SUFFIX}`,
    idSuffix: WORKFLOW_RULE_SUFFIX,
    statement: `The statuses, actions, permissions, reasons, wording keys and notification decisions live only in ${definitionClass} (one TransitionCatalog); no code switches on a status to decide an action. Change the flow with the new-transition skill (tools/scaffold/new-transition.mjs).`,
    enforcedBy: [...WORKFLOW_RULE_ENFORCED_BY],
  };
}
