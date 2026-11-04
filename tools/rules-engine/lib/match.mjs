// Path and content matching shared by the compiler, the hooks and CI tools.
// Consumers read rules/rules.compiled.json and call these helpers; nobody parses YAML at runtime.

const GLOBSTAR_SLASH = '**/';
const GLOBSTAR = '**';
const REGEX_SPECIALS = /[.+^$()|[\]\\]/;
const FORBIDDEN_RUNTIME_FLAGS = /[gy]/g;
const GLOBAL_FLAG = 'g';

const globCache = new Map();

/**
 * Converts a glob to an anchored RegExp. Supported: `**` (any depth), `*` (within one segment), `?`, `{a,b}`.
 * Paths are compared with forward slashes.
 */
export function globToRegExp(glob) {
  const cached = globCache.get(glob);
  if (cached) {
    return cached;
  }
  const regex = new RegExp(`^${translateGlob(glob)}$`);
  globCache.set(glob, regex);
  return regex;
}

function translateGlob(glob) {
  let source = '';
  let index = 0;
  let braceDepth = 0;
  while (index < glob.length) {
    const rest = glob.slice(index);
    if (rest.startsWith(GLOBSTAR_SLASH)) {
      source += '(?:.*/)?';
      index += GLOBSTAR_SLASH.length;
      continue;
    }
    if (rest.startsWith(GLOBSTAR)) {
      source += '.*';
      index += GLOBSTAR.length;
      continue;
    }
    const char = glob[index];
    source += translateGlobChar(char, braceDepth);
    braceDepth += char === '{' ? 1 : char === '}' && braceDepth > 0 ? -1 : 0;
    index += 1;
  }
  return source;
}

function translateGlobChar(char, braceDepth) {
  switch (char) {
    case '*': return '[^/]*';
    case '?': return '[^/]';
    case '{': return '(?:';
    case '}': return braceDepth > 0 ? ')' : '\\}';
    case ',': return braceDepth > 0 ? '|' : ',';
    default: return REGEX_SPECIALS.test(char) ? `\\${char}` : char;
  }
}

/** True when the repo-relative posix path matches any glob. */
export function matchesAnyGlob(relativePath, globs = []) {
  return globs.some((glob) => globToRegExp(glob).test(relativePath));
}

/** True when a forbidden rule applies to the repo-relative path (appliesTo minus excludes). */
export function ruleAppliesToPath(rule, relativePath) {
  return matchesAnyGlob(relativePath, rule.appliesTo) && !matchesAnyGlob(relativePath, rule.excludes);
}

/** Builds the global RegExp for a forbidden rule (flags g/y from the data are ignored). */
export function forbiddenRuleRegExp(rule) {
  const flags = (rule.flags ?? '').replace(FORBIDDEN_RUNTIME_FLAGS, '');
  return new RegExp(rule.pattern, `${flags}${GLOBAL_FLAG}`);
}

/**
 * Every match of a forbidden rule in `content`.
 * @returns {{ line: number, column: number, text: string }[]} 1-based line and column
 */
export function findViolations(content, rule) {
  const regex = forbiddenRuleRegExp(rule);
  const violations = [];
  for (const match of content.matchAll(regex)) {
    if (match[0].length === 0) {
      continue;
    }
    violations.push({ ...lineAndColumn(content, match.index), text: match[0] });
  }
  return violations;
}

function lineAndColumn(content, offset) {
  const before = content.slice(0, offset);
  const lastNewline = before.lastIndexOf('\n');
  return { line: before.split('\n').length, column: offset - lastNewline };
}

/**
 * The compiled module that owns a repo-relative path (longest folder prefix wins), or undefined for files
 * outside every module (they get the base rules).
 */
export function moduleForPath(compiled, relativePath) {
  let owner;
  for (const module of compiled.modules) {
    const prefix = module.path === '' ? '' : `${module.path}/`;
    if (relativePath.startsWith(prefix) && (!owner || module.path.length > owner.path.length)) {
      owner = module;
    }
  }
  return owner;
}

/** The forbidden rules that apply to a repo-relative path, with their module (base rules outside modules). */
export function forbiddenRulesForPath(compiled, relativePath) {
  const module = moduleForPath(compiled, relativePath);
  const rules = module ? module.forbidden : compiled.base.forbidden;
  return rules.filter((rule) => ruleAppliesToPath(rule, relativePath));
}

/**
 * Evaluates all applicable forbidden rules against a file's content.
 * @returns {{ rule: object, violations: { line: number, column: number, text: string }[] }[]}
 */
export function evaluateFile(compiled, relativePath, content) {
  return forbiddenRulesForPath(compiled, relativePath)
    .map((rule) => ({ rule, violations: findViolations(content, rule) }))
    .filter((result) => result.violations.length > 0);
}
