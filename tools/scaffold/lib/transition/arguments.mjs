// Parses and validates the new-transition command line into a request. Pure: it reads no file. Shape errors are
// usage errors (exit 2); what the module's sources contradict is refused later by the plan (exit 1).
import path from 'node:path';
import { UsageError } from '../cli.mjs';
import { KEBAB_NAME_PATTERN } from '../constants.mjs';
import { refusalForPascalName, refusalForPermissionKey, toPascalCase } from '../names.mjs';
import { DEFAULT_ROOT } from '../workspace.mjs';
import {
  AUDIENCES,
  DEFAULT_REASON,
  DEFAULT_REASON_FIELD,
  I18N_PREFIX_PATTERN,
  LIST_SEPARATOR,
  MAX_TEXT_LENGTH,
  NOTIFY_NONE,
  REASON_FIELD_PATTERN,
  Reasons,
  Recipients,
  Sides,
  TEMPLATE_KEY_PATTERN,
  TransitionFlags,
  TransitionSwitches,
  WORDING_SEPARATOR,
} from './constants.mjs';

const VALUE_FLAGS = new Set(Object.values(TransitionFlags));
const SWITCHES = new Set(Object.values(TransitionSwitches));

/** `--flag value` pairs and switches into an object keyed by the flag name without dashes. */
export function parseTransitionArguments(argv) {
  const values = { root: DEFAULT_ROOT };
  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index];
    if (SWITCHES.has(flag)) {
      values[flag.slice(2)] = true;
      continue;
    }
    if (!VALUE_FLAGS.has(flag)) throw new UsageError(`Unknown argument '${flag}'.`);
    const value = argv[index + 1];
    if (value === undefined || value.startsWith('--')) throw new UsageError(`${flag} needs a value.`);
    values[flag.slice(2)] = flag === TransitionFlags.root ? path.resolve(value) : value;
    index += 1;
  }
  return values;
}

/** "serviceRequests" from "ServiceRequests" (the i18n prefix of a module's keys). */
export function camelCaseOf(pascal) {
  return pascal[0].toLowerCase() + pascal.slice(1);
}

function required(value, flag, why) {
  if (typeof value !== 'string' || value.trim().length === 0) throw new UsageError(`${flag} is required ${why}.`);
  return value.trim();
}

function kebab(value, flag) {
  if (!KEBAB_NAME_PATTERN.test(value)) throw new UsageError(`${flag} '${value}' is not kebab-case (e.g. under-review).`);
  return value;
}

function text(value, flag) {
  if (value.length > MAX_TEXT_LENGTH) throw new UsageError(`${flag} is longer than ${MAX_TEXT_LENGTH} characters.`);
  if (/[\r\n]/.test(value)) throw new UsageError(`${flag} must be one line.`);
  return value;
}

/** One wording for every audience, or exactly three (requester;staff;admin). */
export function parseWording(value, flag) {
  const parts = required(value, flag, 'with --status (the status label)').split(WORDING_SEPARATOR).map((part) => part.trim());
  if (parts.some((part) => part.length === 0)) throw new UsageError(`${flag} has an empty wording.`);
  if (parts.length === 1) return AUDIENCES.map(() => text(parts[0], flag));
  if (parts.length === AUDIENCES.length) return parts.map((part) => text(part, flag));
  throw new UsageError(`${flag} takes one wording or ${AUDIENCES.length} separated by '${WORDING_SEPARATOR}' (${AUDIENCES.join(WORDING_SEPARATOR)}).`);
}

/** The notification decision: { none: true } or { none: false, requester, assignee, templateKey }. */
export function parseNotification(notify, template) {
  const value = required(notify, TransitionFlags.notify, `with --action (${NOTIFY_NONE}, or ${Object.values(Recipients).join(LIST_SEPARATOR)}); every transition decides who is told`);
  if (value === NOTIFY_NONE) {
    if (template !== undefined) throw new UsageError(`${TransitionFlags.template} cannot go with ${TransitionFlags.notify} ${NOTIFY_NONE}.`);
    return { none: true };
  }
  const recipients = value.split(LIST_SEPARATOR).map((part) => part.trim());
  const unknown = recipients.filter((recipient) => !Object.values(Recipients).includes(recipient));
  if (unknown.length > 0) throw new UsageError(`${TransitionFlags.notify} '${unknown.join(LIST_SEPARATOR)}' is not ${NOTIFY_NONE} or a list of ${Object.values(Recipients).join(', ')}.`);
  const templateKey = required(template, TransitionFlags.template, `when ${TransitionFlags.notify} names recipients`);
  if (!TEMPLATE_KEY_PATTERN.test(templateKey)) throw new UsageError(`${TransitionFlags.template} '${templateKey}' is not a dotted kebab key (e.g. service-requests.escalated).`);
  return {
    none: false,
    requester: recipients.includes(Recipients.requester),
    assignee: recipients.includes(Recipients.assignee),
    templateKey,
  };
}

function statusRequest(options) {
  if (options.status === undefined) {
    if (options.terminal) throw new UsageError(`${TransitionSwitches.terminal} needs ${TransitionFlags.status}.`);
    return null;
  }
  const key = kebab(required(options.status, TransitionFlags.status, ''), TransitionFlags.status);
  return {
    key,
    constName: toPascalCase(key),
    terminal: Boolean(options.terminal),
    wordingEn: parseWording(options['status-en'], TransitionFlags.statusEn),
    wordingAr: parseWording(options['status-ar'], TransitionFlags.statusAr),
  };
}

function reasonOf(options) {
  const reason = options.reason ?? DEFAULT_REASON;
  if (!Object.hasOwn(Reasons, reason)) throw new UsageError(`${TransitionFlags.reason} '${reason}' is not one of ${Object.keys(Reasons).join(', ')}.`);
  if (reason === DEFAULT_REASON) {
    if (options['reason-field'] !== undefined) throw new UsageError(`${TransitionFlags.reasonField} needs ${TransitionFlags.reason} optional or required.`);
    return { reason, reasonField: null };
  }
  const field = options['reason-field'] ?? DEFAULT_REASON_FIELD;
  if (!REASON_FIELD_PATTERN.test(field)) throw new UsageError(`${TransitionFlags.reasonField} '${field}' is not a camelCase form field name.`);
  return { reason, reasonField: field };
}

function ruleRequest(options) {
  if (options.action === undefined) {
    for (const flag of [TransitionFlags.from, TransitionFlags.to, TransitionFlags.permission, TransitionFlags.side, TransitionFlags.notify]) {
      if (options[flag.slice(2)] !== undefined) throw new UsageError(`${flag} needs ${TransitionFlags.action}.`);
    }
    return null;
  }
  const actionKey = kebab(required(options.action, TransitionFlags.action, ''), TransitionFlags.action);
  const fromKeys = required(options.from, TransitionFlags.from, 'with --action (one status key, or several separated by commas)')
    .split(LIST_SEPARATOR).map((key) => kebab(key.trim(), TransitionFlags.from));
  if (new Set(fromKeys).size !== fromKeys.length) throw new UsageError(`${TransitionFlags.from} lists a status twice.`);
  const toKey = kebab(required(options.to, TransitionFlags.to, 'with --action'), TransitionFlags.to);
  if (fromKeys.includes(toKey)) throw new UsageError(`${TransitionFlags.from} and ${TransitionFlags.to} name the same status '${toKey}'.`);
  const permissionKey = required(options.permission, TransitionFlags.permission, 'with --action (a key of the module\'s permission catalog)');
  const permissionRefusal = refusalForPermissionKey(permissionKey);
  if (permissionRefusal) throw new UsageError(permissionRefusal);
  const side = required(options.side, TransitionFlags.side, `with --action (${Object.keys(Sides).join(', ')})`);
  if (!Object.hasOwn(Sides, side)) throw new UsageError(`${TransitionFlags.side} '${side}' is not one of ${Object.keys(Sides).join(', ')}.`);
  return {
    actionKey,
    actionConst: toPascalCase(actionKey),
    fromKeys,
    toKey,
    permissionKey,
    side,
    ...reasonOf(options),
    owner: Boolean(options.owner),
    labelEn: options['label-en'] === undefined ? null : text(options['label-en'].trim(), TransitionFlags.labelEn),
    labelAr: options['label-ar'] === undefined ? null : text(options['label-ar'].trim(), TransitionFlags.labelAr),
    notification: parseNotification(options.notify, options.template),
  };
}

/**
 * The validated request.
 * @returns {{ module: string, root: string, i18nPrefix: string, dryRun: boolean, generate: boolean, skipGenerate: boolean,
 *   status: object | null, rule: object | null }}
 */
export function transitionRequest(options) {
  const module = required(options.module, TransitionFlags.module, '(the record type\'s module, e.g. ServiceRequests)');
  const moduleRefusal = refusalForPascalName(module, 'The module name');
  if (moduleRefusal) throw new UsageError(moduleRefusal);
  const i18nPrefix = options['i18n-prefix'] ?? camelCaseOf(module);
  if (!I18N_PREFIX_PATTERN.test(i18nPrefix)) throw new UsageError(`${TransitionFlags.i18nPrefix} '${i18nPrefix}' is not a camelCase key.`);
  const status = statusRequest(options);
  const rule = ruleRequest(options);
  if (status === null && rule === null) throw new UsageError(`Give ${TransitionFlags.status} (a new status), ${TransitionFlags.action} (a new transition), or both.`);
  if (status?.terminal && rule?.fromKeys.includes(status.key)) {
    throw new UsageError(`The new status '${status.key}' is terminal; no transition may leave it.`);
  }
  return {
    module,
    root: options.root,
    i18nPrefix,
    dryRun: Boolean(options['dry-run']),
    generate: !options['no-gen'] && !options['skip-generate'],
    skipGenerate: Boolean(options['skip-generate']),
    status,
    rule,
  };
}
