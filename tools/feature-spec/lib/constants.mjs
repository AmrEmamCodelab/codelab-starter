// Every name, pattern, limit and message of the feature-spec validator. No other file in tools/feature-spec holds a
// literal of these. The format itself is documented in docs/features/TEMPLATE.md and docs/features/README.md.

/** Process exit codes of check-spec.mjs. */
export const ExitCode = Object.freeze({
  /** The spec is valid (warnings may have been printed). */
  ok: 0,
  /** At least one error-severity finding. */
  findings: 1,
  /** Bad arguments or an unreadable file. */
  usage: 2,
});

/** Finding severities. Only errors fail the check; warnings are reported and counted. */
export const Severity = Object.freeze({
  error: 'error',
  warning: 'warning',
});

/** The lifecycle values of the front-matter `status` key. */
export const SpecStatus = Object.freeze({
  draft: 'draft',
  approved: 'approved',
});

/** Front-matter keys, all required, in the documented order. */
export const FrontMatterKey = Object.freeze({
  feature: 'feature',
  status: 'status',
  approvedBy: 'approvedBy',
  approvedOn: 'approvedOn',
  inputs: 'inputs',
});
export const REQUIRED_FRONT_MATTER_KEYS = Object.freeze(Object.values(FrontMatterKey));

/**
 * The approvedBy a headless pipeline proof records (tools/pipeline-proofs, -PreApproveSpec). Accepted only when
 * PRE_APPROVAL_VARIABLE is PRE_APPROVAL_ENABLED in the checker's environment, which Invoke-PipelineProof.ps1 sets for
 * its own throwaway run; anywhere else (a real project, CI) a human approves the spec.
 */
export const PRE_APPROVED_BY = 'pipeline-proof (pre-approved)';
export const PRE_APPROVAL_VARIABLE = 'CODELAB_PIPELINE_PROOF_APPROVAL';
export const PRE_APPROVAL_ENABLED = '1';
/** Words that mark an approver as automated rather than a person (whole words, any case). */
export const AUTOMATED_APPROVER = /\b(?:agent|agents|claude|bot|ai|assistant|automated|automation|auto|pipeline|llm|model|subagent)\b/i;
/**
 * Look-alike characters folded before the approver checks, so "C1aude" or "B0t" still read as automated. The digit
 * one is tried as both letters it imitates.
 */
export const LOOKALIKE_CHARACTERS = Object.freeze({ 0: 'o', 3: 'e', 4: 'a', 5: 's', 7: 't', 8: 'b', '@': 'a', $: 's' });
export const DIGIT_ONE = '1';
export const DIGIT_ONE_LOOKALIKES = Object.freeze(['l', 'i']);
/**
 * Role and placeholder words. An approver made only of these ("Platform Owner", "Spec Writer", "The Reviewer") names
 * no person; a person's name with a role beside it ("Sara Al-Harbi (platform owner)") is fine.
 */
export const ROLE_ONLY_WORDS = Object.freeze(new Set([
  'a', 'an', 'the', 'and', 'of', 'for', 'by', 'platform', 'product', 'project', 'owner', 'owners', 'team', 'lead',
  'manager', 'reviewer', 'reviewers', 'approver', 'approved', 'admin', 'administrator', 'system', 'user', 'human',
  'person', 'tester', 'qa', 'writer', 'builder', 'designer', 'keeper', 'verifier', 'gate', 'spec', 'feature', 'tool',
  'tools', 'script', 'ci', 'build', 'pre', 'unknown', 'tbd', 'n', 'na', 'none', 'someone', 'anyone', 'me',
]));
/** Where the agent definitions live (their file names are agent names, which are never approvers). */
export const AGENTS_FOLDER = '.claude/agents';
export const AGENT_FILE_EXTENSION = '.md';

/** The front-matter fence line. */
export const FRONT_MATTER_FENCE = '---';
/** Fenced code block opener (``` or ~~~); headings and tables inside a fence are ignored. */
export const CODE_FENCE = /^\s*(```|~~~)/;
/** A level-2 heading. */
export const H2_HEADING = /^##\s+(.+?)\s*#*\s*$/;
/** A front-matter `key: value` line. */
export const FRONT_MATTER_ENTRY = /^([A-Za-z][A-Za-z0-9_-]*):(?:\s+(.*))?$/;
/** A front-matter block-list item (`  - value`). */
export const FRONT_MATTER_LIST_ITEM = /^\s+-\s+(.*)$/;
/** A front-matter flow list (`[a, b]`). */
export const FRONT_MATTER_FLOW_LIST = /^\[(.*)\]$/;
/** A YAML comment or blank line. */
export const FRONT_MATTER_IGNORABLE = /^\s*(#.*)?$/;
/** approvedOn is an ISO calendar date. */
export const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
/** The `feature` value is a kebab-case slug (it is also the file name). */
export const KEBAB_SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

/** Section names (H2), in the required order. */
export const Section = Object.freeze({
  scenario: 'Scenario',
  inputs: 'Inputs',
  modules: 'Modules',
  transitions: 'Statuses and transitions',
  deadlines: 'Deadlines',
  permissions: 'Permissions',
  endpoints: 'Endpoints',
  screens: 'Screens',
  ruleFiles: 'Rule files',
  tests: 'Tests',
  docs: 'Docs',
  skillPlan: 'Skill plan',
  handEditLog: 'Hand-edit log',
  reviewLog: 'Review log',
});
export const REQUIRED_SECTIONS = Object.freeze(Object.values(Section));

/** Required table columns per section (compared case-insensitively, in order). */
export const TableColumns = Object.freeze({
  [Section.transitions]: Object.freeze(['From', 'Action', 'To', 'Permission', 'Reason required']),
  [Section.endpoints]: Object.freeze(['Method', 'Route', 'Permission', 'Request', 'Response']),
  [Section.screens]: Object.freeze(['Route', 'Page pattern', 'Permission', 'Target image']),
  [Section.handEditLog]: Object.freeze(['#', 'File', 'Reason']),
});

/** Column indexes the value checks read. */
export const TransitionColumn = Object.freeze({ reasonRequired: 4 });
export const EndpointColumn = Object.freeze({ method: 0, route: 1 });
export const ScreenColumn = Object.freeze({ route: 0, pagePattern: 1 });
export const HandEditColumn = Object.freeze({ number: 0 });

/** Allowed "Reason required" values. */
export const REASON_REQUIRED_VALUES = Object.freeze(['yes', 'no']);
/** Allowed endpoint methods. */
export const HTTP_METHODS = Object.freeze(['GET', 'POST', 'PUT', 'PATCH', 'DELETE']);
/** Routes (endpoints and screens) start with a slash. */
export const ROUTE_PREFIX = '/';
/** The 8 page patterns (docs/design/page-patterns.md, docs/design/patterns/<id>.md). */
export const PAGE_PATTERNS = Object.freeze([
  'admin-list',
  'tree-editor',
  'work-queue',
  'staff-detail',
  'wizard',
  'requester-detail',
  'portal-content',
  'landing-dashboard',
]);

/** Skill-plan entries: the scaffold skills plus the deadline recipe (docs/recipes/add-deadline.md). */
export const PlanSkill = Object.freeze({
  newModule: 'new-module',
  newTransition: 'new-transition',
  deadlineRecipe: 'deadline-recipe',
  newPermission: 'new-permission',
  newLookup: 'new-lookup',
  newEndpoint: 'new-endpoint',
  newScreen: 'new-screen',
});
/** The canonical order the pipeline runs them in (build-feature step 4). */
export const PLAN_SKILLS = Object.freeze(Object.values(PlanSkill));
/** An ordered-list item (`1. ...` or `1) ...`). */
export const ORDERED_LIST_ITEM = /^\s*\d+[.)]\s+(.*)$/;
/** The first word of a plan item, optionally in backticks or bold. */
export const PLAN_ITEM_SKILL = /^(?:\*\*|`)?([A-Za-z][A-Za-z0-9-]*)(?:`|\*\*)?/;

/** A section whose whole content is "None" (optionally followed by a reason) declares nothing. */
export const NONE_CONTENT = /^none\b/i;

/** The proof exit criterion: at most this many hand edits after approval. More is a warning. */
export const MAX_HAND_EDITS = 10;
/** The review-log line that records the review iteration count. */
export const ITERATIONS_LINE = /^\s*(?:[-*]\s+)?(?:\*\*)?Iterations(?:\*\*)?\s*:\s*(?:\*\*)?\s*(\d+)\b/i;

/** Markdown table syntax. */
export const TABLE_ROW = /^\s*\|/;
export const TABLE_SEPARATOR_CELL = /^:?-{3,}:?$/;
export const TABLE_CELL_DELIMITER = '|';
export const ESCAPED_PIPE = '\\|';
export const ESCAPED_PIPE_PLACEHOLDER = '\u0000';

/** Finding rule ids. */
export const RuleId = Object.freeze({
  frontMatterMissing: 'front-matter.missing',
  frontMatterSyntax: 'front-matter.syntax',
  frontMatterKeyMissing: 'front-matter.key-missing',
  frontMatterUnknownKey: 'front-matter.unknown-key',
  featureSlug: 'front-matter.feature-slug',
  statusInvalid: 'front-matter.status-invalid',
  approvalIncomplete: 'front-matter.approval-incomplete',
  approvedOnInvalid: 'front-matter.approved-on-invalid',
  inputsInvalid: 'front-matter.inputs-invalid',
  approvalRequired: 'approval.required',
  approvalPreApproved: 'approval.pre-approved-outside-proof',
  approvalNotHuman: 'approval.not-human',
  sectionMissing: 'section.missing',
  sectionOrder: 'section.order',
  sectionDuplicate: 'section.duplicate',
  sectionUnknown: 'section.unknown',
  sectionEmpty: 'section.empty',
  tableMissing: 'table.missing',
  tableColumns: 'table.columns',
  tableCellEmpty: 'table.cell-empty',
  tableCellCount: 'table.cell-count',
  tableValue: 'table.value-invalid',
  skillPlanEmpty: 'skill-plan.empty',
  skillPlanUnknownSkill: 'skill-plan.unknown-skill',
  skillPlanModuleFirst: 'skill-plan.module-first',
  skillPlanCoverage: 'skill-plan.coverage',
  handEditsTooMany: 'hand-edits.too-many',
  reviewIterationsMissing: 'review-log.iterations-missing',
});

/** CLI options and usage. */
export const CliOption = Object.freeze({
  requireApproved: '--require-approved',
  json: '--json',
});
export const USAGE = `Usage: node tools/feature-spec/check-spec.mjs <spec.md> [${CliOption.requireApproved}] [${CliOption.json}]`;
export const UTF8 = 'utf8';
export const JSON_INDENT = 2;
/** A hand-edit log "#" cell. */
export const HAND_EDIT_NUMBER = /^\d+$/;
/** Inline-code marks are ignored when a cell value is compared. */
export const INLINE_CODE_MARK = '`';
/** HTML comments (one or several lines) do not count as section content. */
export const HTML_COMMENT_OPEN = '<!--';
export const HTML_COMMENT_CLOSE = '-->';
