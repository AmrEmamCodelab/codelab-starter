// The MODULE.md note new-transition adds: one bullet per status or transition it wrote, under the
// `<!-- scaffold:workflow -->` marker when the file has one, else at the end of its "Use" section. Pure; idempotent.
import { NEWLINE } from '../constants.mjs';
import { WorkflowMarkers } from './constants.mjs';
import { labelKey, wordingKey } from './entries.mjs';

const USE_HEADING = /^#{1,6}\s+Use\s*$/im;
const NEXT_HEADING = /^#{1,6}\s+\S/m;
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

/** Inserts `lines` (each once) into the MODULE.md text. */
export function addModuleDocNotes(doc, lines) {
  const missing = lines.filter((line) => !doc.includes(line));
  if (missing.length === 0) return doc;
  const eol = doc.includes('\r\n') ? '\r\n' : NEWLINE;
  const block = missing.join(eol) + eol;
  const marker = doc.indexOf(WorkflowMarkers.moduleDoc);
  if (marker >= 0) return doc.slice(0, marker) + block + doc.slice(marker);
  const use = USE_HEADING.exec(doc);
  if (!use) return `${doc.replace(/\s*$/, '')}${eol}${eol}${block}`;
  const afterUse = use.index + use[0].length;
  const next = NEXT_HEADING.exec(doc.slice(afterUse));
  if (!next) return `${doc.replace(/\s*$/, '')}${eol}${block}`;
  const sectionEnd = afterUse + next.index;
  const body = doc.slice(0, sectionEnd).replace(/\s*$/, '');
  return `${body}${eol}${block}${eol}${doc.slice(sectionEnd)}`;
}
