// Every name, pattern, path and message of new-transition (module 8: a status and/or a transition in a record
// type's workflow). Shared scaffold constants (module paths, markers of other generators) stay in ../constants.mjs.
//
// RENAME SAFETY (ADR-003): starter-named literals are written whole so a fork's rename rewrites them.

/** Value flags new-transition takes. */
export const TransitionFlags = Object.freeze({
  module: '--module',
  status: '--status',
  statusEn: '--status-en',
  statusAr: '--status-ar',
  action: '--action',
  from: '--from',
  to: '--to',
  permission: '--permission',
  side: '--side',
  reason: '--reason',
  reasonField: '--reason-field',
  labelEn: '--label-en',
  labelAr: '--label-ar',
  notify: '--notify',
  template: '--template',
  i18nPrefix: '--i18n-prefix',
  root: '--root',
});

/** Switches new-transition takes. */
export const TransitionSwitches = Object.freeze({
  terminal: '--terminal',
  owner: '--owner',
  dryRun: '--dry-run',
  noGenerate: '--no-gen',
  skipGenerate: '--skip-generate',
  help: '--help',
});

/** TransitionSide (Starter.Domain.Workflow) members by CLI value; the C# enum is the source of the numbers. */
export const Sides = Object.freeze({ requester: 'Requester', staff: 'Staff', system: 'System' });

/** ReasonRequirement (Starter.Domain.Workflow) members by CLI value. */
export const Reasons = Object.freeze({ none: 'None', optional: 'Optional', required: 'Required' });
export const DEFAULT_REASON = 'none';
/** The reason field used when --reason is optional or required and --reason-field is not given. */
export const DEFAULT_REASON_FIELD = 'reason';

/** --notify values: `none` (the explicit decision to notify nobody) or a comma list of recipients. */
export const NOTIFY_NONE = 'none';
export const Recipients = Object.freeze({ requester: 'requester', assignee: 'assignee' });
export const LIST_SEPARATOR = ',';
/** --status-en / --status-ar: one wording for every audience, or three (requester;staff;admin). */
export const WORDING_SEPARATOR = ';';
export const AUDIENCES = Object.freeze(['requester', 'staff', 'admin']);
/** WorkflowAudience (Starter.Domain.Workflow) members, in AUDIENCES order. */
export const AUDIENCE_MEMBERS = Object.freeze(['Requester', 'Staff', 'Admin']);

/** i18n key segments: <prefix>.status.<statusKey>.<audience> and <prefix>.action.<actionKey> (shared contract). */
export const I18N_STATUS_SEGMENT = 'status';
export const I18N_ACTION_SEGMENT = 'action';
export const I18N_SEPARATOR = '.';
export const I18N_BUNDLES = Object.freeze({
  en: 'frontend/starter-web/src/assets/i18n/en.json',
  ar: 'frontend/starter-web/src/assets/i18n/ar.json',
});
export const JSON_INDENT = 2;

/** Template key format (notification templates are dotted kebab keys like permission keys). */
export const TEMPLATE_KEY_PATTERN = /^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$/;
export const I18N_PREFIX_PATTERN = /^[a-z][A-Za-z0-9]*$/;
/** A reason form field (camelCase, like the wizard's field names). */
export const REASON_FIELD_PATTERN = /^[a-z][A-Za-z0-9]*$/;
export const MAX_TEXT_LENGTH = 200;

/** How the generator finds the record type's workflow classes inside the module folder (any .cs file). */
export const SourcePatterns = Object.freeze({
  statusesClass: /\bstatic\s+class\s+(\w+Statuses)\b/g,
  statusKeysClass: /\bstatic\s+class\s+(\w+StatusKeys)\b/g,
  actionsClass: /\bstatic\s+class\s+(\w+Actions)\b/g,
  notificationKeysClass: /\bstatic\s+class\s+(\w+NotificationKeys)\b/g,
  definitionClass: /\bclass\s+(\w+)\s*(?:\([^)]*\))?\s*:\s*(?:[\w.]+\s*,\s*)*IRecordWorkflowDefinition\b/g,
  namespace: /^namespace\s+([\w.]+)\s*;/m,
  intConstant: /\bconst\s+int\s+(\w+)\s*=\s*(-?\d+)\s*;/g,
  stringConstant: /\bconst\s+string\s+(\w+)\s*=\s*"([^"]*)"\s*;/g,
  wordingHelper: /\bstatic\s+string\s+WordingKey\s*\(\s*string\s+\w+\s*,\s*WorkflowAudience\s+\w+\s*\)/,
  labelHelper: /\bstatic\s+string\s+LabelKey\s*\(\s*string\s+\w+\s*\)/,
});

/** List markers in the workflow class (preferred), and the property lists used when a file has none. */
export const WorkflowMarkers = Object.freeze({
  statuses: '// scaffold:workflow-statuses',
  rules: '// scaffold:workflow-rules',
  moduleDoc: '<!-- scaffold:workflow -->',
});
export const ListProperties = Object.freeze({ statuses: 'Statuses', rules: 'Rules' });

/** C# names the generated code uses (Starter.Domain.Workflow / Starter.Workflow.Testing). */
export const CSharp = Object.freeze({
  workflowNamespace: 'Starter.Domain.Workflow',
  applicationWorkflowNamespace: 'Starter.Application.Workflow',
  testingNamespace: 'Starter.Workflow.Testing',
  indent: '    ',
});

/** Where things live, relative to the repo root. */
export const TransitionPaths = Object.freeze({
  moduleDoc: (module) => `backend/src/Modules/${module}/MODULE.md`,
  testsFolder: (module) => `backend/tests/Modules/Starter.Modules.${module}.Tests`,
  testProject: (module) => `backend/tests/Modules/Starter.Modules.${module}.Tests/Starter.Modules.${module}.Tests.csproj`,
  testFile: (module, testClass) => `backend/tests/Modules/Starter.Modules.${module}.Tests/Workflow/${testClass}.cs`,
  /** The test kit as the module test project references it ('/'-separated; written with the csproj's '\'). */
  kitReference: '../../Starter.Workflow.Testing/Starter.Workflow.Testing.csproj',
  kitProjectName: 'Starter.Workflow.Testing.csproj',
});

/** Template sets under tools/scaffold/templates. */
export const TransitionTemplates = Object.freeze({ status: 'transition/status', rule: 'transition/rule' });

/** Folders never scanned for module sources. */
export const SKIPPED_FOLDERS = Object.freeze(['bin', 'obj']);
export const CSHARP_EXTENSION = '.cs';

/** Report prefixes only new-transition prints. */
export const DryRunReport = Object.freeze({ wouldCreate: 'would create', wouldChange: 'would change' });
