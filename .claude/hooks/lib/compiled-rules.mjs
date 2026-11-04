// Loads rules/rules.compiled.json and normalizes it into the shape the hooks evaluate.
// The rules engine owns the file format; this loader accepts the documented shape (see ../README.md) and a few
// tolerated aliases so the hooks do not break while the engine's format settles. Hooks never parse YAML.
import fs from 'node:fs';
import path from 'node:path';
import { DefaultGeneratedGlobs, DefaultModuleRoots, RepoPaths } from './constants.mjs';

const RULE_KINDS = Object.freeze({ Forbidden: 'forbidden', Required: 'required' });
const FILE_GLOB_KEYS = Object.freeze(['files', 'appliesTo', 'include', 'paths']);
const EXCLUDE_KEYS = Object.freeze(['excludes', 'exclude']);
const FIX_KEYS = Object.freeze(['fix', 'fixMessage']);
const MESSAGE_KEYS = Object.freeze(['message', 'description']);
const PATTERN_KEYS = Object.freeze(['pattern', 'regex']);
const MODULE_NAME_KEYS = Object.freeze(['module', 'name']);
const MODULE_PATH_KEYS = Object.freeze(['path', 'root', 'dir']);
const MODULE_SUMMARY_KEYS = Object.freeze(['summary', 'description']);
// Flags the evaluator controls itself (it always adds 'g'); a sticky flag would break whole-file matching.
const RUNTIME_FLAGS = /[gy]/g;
const ALL_FILES_GLOB = '**';
/** Rule severities: an error fails CI; a warning is reported only. Hooks refuse both (no new debt). */
export const RULE_SEVERITIES = Object.freeze({ Error: 'error', Warning: 'warning' });
const DEFAULT_SEVERITY = RULE_SEVERITIES.Error;

function firstDefined(source, keys) {
  for (const key of keys) {
    if (source?.[key] !== undefined) return source[key];
  }
  return undefined;
}

function asArray(value) {
  if (value === undefined || value === null) return [];
  return Array.isArray(value) ? value : [value];
}

function normalizeRule(rawRule, kind, origin) {
  const pattern = firstDefined(rawRule, PATTERN_KEYS);
  if (!rawRule?.id || !pattern) return null;
  const files = asArray(firstDefined(rawRule, FILE_GLOB_KEYS));
  return {
    id: rawRule.id,
    kind,
    origin,
    pattern,
    flags: (rawRule.flags ?? '').replace(RUNTIME_FLAGS, ''),
    files: files.length > 0 ? files : [ALL_FILES_GLOB],
    exclude: asArray(firstDefined(rawRule, EXCLUDE_KEYS)),
    message: firstDefined(rawRule, MESSAGE_KEYS) ?? '',
    fix: firstDefined(rawRule, FIX_KEYS) ?? '',
    severity: rawRule.severity ?? DEFAULT_SEVERITY,
  };
}

/** Rules without a pattern (prose-only `required` statements enforced by tests or review) are skipped. */

function normalizeRuleList(source, origin) {
  const forbidden = asArray(source?.forbidden).map((rule) => normalizeRule(rule, RULE_KINDS.Forbidden, origin));
  const required = asArray(source?.required).map((rule) => normalizeRule(rule, RULE_KINDS.Required, origin));
  return [...forbidden, ...required].filter(Boolean);
}

function moduleEntries(rawModules) {
  if (Array.isArray(rawModules)) return rawModules.map((module) => [firstDefined(module, MODULE_NAME_KEYS), module]);
  return Object.entries(rawModules ?? {});
}

/** A compiled module's rules are its EFFECTIVE rules: inherited base rules minus exemptions, plus its own. */
function normalizeModule([name, rawModule]) {
  const modulePath = firstDefined(rawModule, MODULE_PATH_KEYS);
  if (!name || !modulePath) return null;
  return {
    name,
    kind: rawModule.kind ?? '',
    path: String(modulePath).replace(/\\/g, '/').replace(/\/$/, ''),
    rules: normalizeRuleList(rawModule, `module:${name}`),
    summary: rawModule.docs?.summary ?? firstDefined(rawModule, MODULE_SUMMARY_KEYS) ?? '',
  };
}

function normalizeModuleRoots(rawRoots) {
  const roots = asArray(rawRoots)
    .map((root) => (typeof root === 'string' ? { kind: '', path: root } : root))
    .filter((root) => root?.path)
    .map((root) => ({ kind: root.kind ?? '', path: root.path.replace(/\\/g, '/').replace(/\/$/, '') }));
  return roots.length > 0 ? roots : DefaultModuleRoots.map((root) => ({ ...root }));
}

/** Normalizes an already-parsed compiled-rules object. Exported for tests. */
export function normalizeCompiledRules(raw) {
  return {
    schemaVersion: raw?.schemaVersion ?? null,
    moduleRoots: normalizeModuleRoots(raw?.moduleRoots),
    generatedGlobs: [...DefaultGeneratedGlobs, ...asArray(raw?.generatedFiles)],
    baseRules: normalizeRuleList(raw?.base, 'base'),
    modules: moduleEntries(raw?.modules).map(normalizeModule).filter(Boolean),
  };
}

/**
 * Reads rules.compiled.json under the project root.
 * Returns { status: 'ok' | 'missing' | 'invalid', rules, error? }. On 'missing'/'invalid' the rules hold the
 * defaults (module roots, generated globs) and no forbidden/required rules, so the hooks still guard paths.
 */
export function loadCompiledRules(projectRoot) {
  const file = path.join(projectRoot, RepoPaths.CompiledRules);
  if (!fs.existsSync(file)) return { status: 'missing', rules: normalizeCompiledRules(null) };
  try {
    const raw = JSON.parse(fs.readFileSync(file, 'utf8'));
    return { status: 'ok', rules: normalizeCompiledRules(raw) };
  } catch (error) {
    return { status: 'invalid', rules: normalizeCompiledRules(null), error: error.message };
  }
}

/**
 * Locates the module that owns a repo-relative path.
 * Returns { root, name, folder, compiled } where compiled is the module entry from the compiled rules or null
 * (for example a new module not compiled yet). Returns null when the path is not inside a module folder.
 * A file directly inside a module root (not in a module folder) is not in a module.
 */
export function findModuleForPath(relativePath, rules) {
  const lowerPath = relativePath.toLowerCase();
  for (const root of rules.moduleRoots) {
    const prefix = `${root.path.toLowerCase()}/`;
    if (!lowerPath.startsWith(prefix)) continue;
    const segments = relativePath.slice(prefix.length).split('/');
    if (segments.length < 2) return null;
    const folder = `${root.path}/${segments[0]}`;
    const compiled = rules.modules.find((module) => module.path.toLowerCase() === folder.toLowerCase()) ?? null;
    return { root, name: segments[0], folder, compiled };
  }
  const owners = rules.modules.filter((module) => lowerPath.startsWith(`${module.path.toLowerCase()}/`));
  const compiled = owners.sort((left, right) => right.path.length - left.path.length)[0];
  return compiled ? { root: null, name: compiled.name, folder: compiled.path, compiled } : null;
}

/**
 * The rules for a path's module: a compiled module's own effective list (the engine already merged the base
 * rules into it and removed the module's exemptions); the base rules for files outside modules and for modules
 * not compiled yet.
 */
export function rulesForModule(rules, module) {
  return module?.compiled ? module.compiled.rules : rules.baseRules;
}

export { RULE_KINDS };
