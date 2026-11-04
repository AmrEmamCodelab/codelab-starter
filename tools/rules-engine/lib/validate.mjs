// Checks the schema cannot express: regexes compile, ids are unique and correctly prefixed, exemptions point at
// real base rules, dependency lists do not contradict, banned symbols are not repeated.
import { BASE_RULE_PREFIX, MODULE_KINDS } from './constants.mjs';

const RULE_LISTS = Object.freeze(['required', 'forbidden', 'bannedSymbols']);
const RULE_ID_SEPARATOR = '.';
const BANNED_LINE_SEPARATOR = ';';
const COMMENT_PREFIX = '//';

/** All rule ids declared in a rules file, in declaration order. */
export function declaredRuleIds(rules) {
  return RULE_LISTS.flatMap((list) => (rules[list] ?? []).map((rule) => rule.id));
}

/** The id prefix every rule in a module file must use: the lower-cased module name. */
export function moduleRulePrefix(moduleName) {
  return moduleName.toLowerCase();
}

/** Reports duplicate ids, ids with the wrong prefix and regexes that do not compile. */
export function checkRuleIds(rules, expectedPrefix, file, problems) {
  const seen = new Set();
  for (const id of declaredRuleIds(rules)) {
    if (seen.has(id)) {
      problems.add(file, `rule id '${id}' is declared more than once`);
    }
    seen.add(id);
    if (!id.startsWith(`${expectedPrefix}${RULE_ID_SEPARATOR}`)) {
      problems.add(file, `rule id '${id}' must start with '${expectedPrefix}${RULE_ID_SEPARATOR}'`);
    }
  }
}

const UNBOUNDED_QUANTIFIER = /^(?:[*+]|\{\d+,\})/;
const ESCAPE = '\\';

/**
 * True when the pattern repeats a group that itself contains an unbounded quantifier, e.g. (\w+\s?)* or (a*)+.
 * Such nesting backtracks exponentially on a near-miss input (ReDoS) and would stall the hooks and CI.
 * A lint, not a proof: escapes and character classes are skipped; bounded repeats ({2,5}, ?) are allowed.
 */
export function hasNestedUnboundedQuantifier(pattern) {
  const groups = [];
  let insideClass = false;
  for (let index = 0; index < pattern.length; index += 1) {
    const char = pattern[index];
    if (char === ESCAPE) { index += 1; continue; }
    if (insideClass) { insideClass = char !== ']'; continue; }
    if (char === '[') { insideClass = true; continue; }
    if (char === '(') { groups.push(false); continue; }
    const rest = pattern.slice(index + 1);
    if (char === ')') {
      const innerUnbounded = groups.pop() ?? false;
      if (innerUnbounded && UNBOUNDED_QUANTIFIER.test(rest)) return true;
      if (innerUnbounded && groups.length > 0) groups[groups.length - 1] = true;
      continue;
    }
    if (groups.length > 0 && UNBOUNDED_QUANTIFIER.test(pattern.slice(index))) groups[groups.length - 1] = true;
  }
  return false;
}

/** Reports forbidden rules whose pattern is not a valid JavaScript regex or can backtrack catastrophically. */
export function checkPatterns(rules, file, problems) {
  for (const rule of rules.forbidden ?? []) {
    try {
      new RegExp(rule.pattern, rule.flags ?? '');
    } catch (error) {
      problems.add(file, `forbidden rule '${rule.id}' has an invalid pattern: ${error.message}`);
      continue;
    }
    if (hasNestedUnboundedQuantifier(rule.pattern)) {
      problems.add(file, `forbidden rule '${rule.id}' repeats a group that contains an unbounded quantifier `
        + '(catastrophic backtracking); rewrite it without nested * or +');
    }
  }
}

/**
 * Reports recipes (docs every module's CLAUDE.md points to) that do not exist, so a dead reference never reaches
 * the generated files. Recipe paths are repo-relative.
 * @param {(relativePath: string) => boolean} exists
 */
export function checkRecipes(rules, exists, file, problems) {
  for (const recipe of rules.recipes ?? []) {
    if (!exists(recipe)) problems.add(file, `recipe '${recipe}' does not exist`);
  }
}

/** Reports exemptions of unknown or repeated base rules. */
export function checkExemptions(moduleRules, baseIds, file, problems) {
  const seen = new Set();
  for (const exemption of moduleRules.exemptions ?? []) {
    if (!exemption.rule.startsWith(`${BASE_RULE_PREFIX}${RULE_ID_SEPARATOR}`) || !baseIds.has(exemption.rule)) {
      problems.add(file, `exemption '${exemption.rule}' does not name a base rule`);
    }
    if (seen.has(exemption.rule)) {
      problems.add(file, `exemption '${exemption.rule}' is listed more than once`);
    }
    seen.add(exemption.rule);
  }
}

/** Reports a module that depends on itself or lists a module as both allowed and forbidden. */
export function checkDependencies(moduleRules, file, problems) {
  const allowed = new Set(moduleRules.mayDependOn ?? []);
  for (const name of moduleRules.mustNotDependOn ?? []) {
    if (allowed.has(name)) {
      problems.add(file, `module '${name}' is in both mayDependOn and mustNotDependOn`);
    }
  }
  const all = [...allowed, ...(moduleRules.mustNotDependOn ?? [])];
  if (all.includes(moduleRules.module)) {
    problems.add(file, `module '${moduleRules.module}' cannot list itself as a dependency`);
  }
}

/**
 * Resolves the module kind: explicit, else dotnet when the folder holds a .csproj, else angular.
 * An explicit `kind: dotnet` is trusted without a .csproj, so a module folder can be scaffolded (rules, MODULE.md and
 * the generated files) before its project lands. `kind: angular` next to a .csproj is a contradiction.
 */
export function resolveModuleKind(moduleRules, hasCsproj, file, problems) {
  if (moduleRules.kind === MODULE_KINDS.angular && hasCsproj) {
    problems.add(file, 'kind is angular but the module folder has a .csproj');
  }
  return moduleRules.kind ?? (hasCsproj ? MODULE_KINDS.dotnet : MODULE_KINDS.angular);
}

/** Parses the SDK global banned list into a Set of doc ids (comments and blank lines skipped). */
export function parseBannedSymbolList(text) {
  const symbols = new Set();
  for (const rawLine of (text ?? '').split(/\r?\n/)) {
    const line = rawLine.trim();
    if (line === '' || line.startsWith(COMMENT_PREFIX)) {
      continue;
    }
    symbols.add(line.split(BANNED_LINE_SEPARATOR)[0].trim());
  }
  return symbols;
}

/** Reports banned symbols repeated in the merged list or already banned globally by Codelab.Build.Sdk. */
export function checkBannedSymbols(bannedSymbols, globalSymbols, file, problems) {
  const seen = new Set();
  for (const entry of bannedSymbols) {
    if (globalSymbols.has(entry.symbol)) {
      problems.add(file, `banned symbol '${entry.symbol}' (${entry.id}) is already banned globally by Codelab.Build.Sdk`);
    }
    if (seen.has(entry.symbol)) {
      problems.add(file, `banned symbol '${entry.symbol}' (${entry.id}) is listed more than once (base + module)`);
    }
    seen.add(entry.symbol);
  }
}

/** Reports two module files that declare the same module name. */
export function checkUniqueModuleNames(modules, problems) {
  const byName = new Map();
  for (const module of modules) {
    const previous = byName.get(module.module);
    if (previous) {
      problems.add(module.rulesFile, `module name '${module.module}' is already used by ${previous}`);
    }
    byName.set(module.module, module.rulesFile);
  }
}
