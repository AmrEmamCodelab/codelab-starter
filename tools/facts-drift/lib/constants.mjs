// Names, rule ids and file locations shared by the facts-drift gate. Every literal the checks compare against lives
// here or in facts-drift.config.json, never inline.
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const TOOL_NAME = 'facts-drift';

/** tools/facts-drift/, where the default configuration files live. */
export const TOOL_DIRECTORY = dirname(dirname(fileURLToPath(import.meta.url)));
export const DEFAULT_CONFIG_FILE = join(TOOL_DIRECTORY, 'facts-drift.config.json');
export const DEFAULT_STALE_TERMS_FILE = join(TOOL_DIRECTORY, 'stale-terms.txt');
export const DEFAULT_ALLOWLIST_FILE = join(TOOL_DIRECTORY, 'allowlist.txt');

/** The scrub sweep's term list and exceptions: reused for "source-product term" text, never copied. */
export const SCRUB_TERMS_FILE = join(TOOL_DIRECTORY, '..', 'extract', 'modhs-terms.txt');
export const SCRUB_EXCEPTIONS_FILE = join(TOOL_DIRECTORY, '..', 'extract', 'scrub-exceptions.txt');

export const DEFAULT_ROOT = '.';
export const JSON_INDENT = 2;

/** Rule ids. The allowlist names these (a trailing "*" selects a prefix). */
export const RuleIds = Object.freeze({
  pathMissing: 'path-missing',
  globEmpty: 'glob-empty',
  npmScriptMissing: 'npm-script-missing',
  npmPackageMissing: 'npm-package-missing',
  commandFileMissing: 'command-file-missing',
  scriptSwitchUnknown: 'script-switch-unknown',
  skillMissing: 'skill-missing',
  agentMissing: 'agent-missing',
  typeUnresolved: 'type-unresolved',
});
/** Stale-term findings are "stale-<id>" (ids from stale-terms.txt). */
export const STALE_RULE_PREFIX = 'stale-';
/** Source-product findings keep the scrub sweep's own ids ("term-<id>"). */
export const SCRUB_TERM_RULE_PREFIX = 'term-';

/** Kinds of named reference ("the `x` skill", "agent `y`"). */
export const ReferenceKinds = Object.freeze({ skill: 'skill', agent: 'agent' });

/** Where skills and agents live, relative to the repository root. */
export const SKILLS_DIRECTORY = '.claude/skills';
export const SKILL_FILE_NAME = 'SKILL.md';
export const AGENTS_DIRECTORY = '.claude/agents';
export const AGENT_FILE_EXTENSION = '.md';

export const PACKAGE_JSON = 'package.json';
export const PATH_SEPARATOR = '/';
export const PARENT_SEGMENT = '..';
