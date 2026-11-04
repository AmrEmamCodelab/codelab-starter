// Inheritance: every module gets the base rules (minus its exemptions) plus its own rules.
import { MODULE_FILES, PATHS, RULE_SOURCE, SEVERITY } from './constants.mjs';
import { joinPosix } from './files.mjs';

/** Forbidden rule with defaults filled in, globs made repo-relative and its origin recorded. */
function normalizeForbidden(rule, source, modulePath) {
  const prefix = (glob) => joinPosix(modulePath, glob);
  return {
    id: rule.id,
    source,
    description: rule.description,
    pattern: rule.pattern,
    flags: rule.flags ?? '',
    appliesTo: rule.appliesTo.map(prefix),
    excludes: (rule.excludes ?? []).map(prefix),
    ...(rule.exemptEmittedFiles ? { exemptEmittedFiles: true } : {}),
    fix: rule.fix,
    severity: rule.severity ?? SEVERITY.error,
  };
}

/**
 * A rule with exemptEmittedFiles skips the engine's own outputs, chosen by PATH: the repo-wide globalconfig (base) and
 * the module's generated .editorconfig (module). A marker in the content would let any hand-written file opt out.
 */
function withEmittedExcludes(rule, emittedPaths) {
  return rule.exemptEmittedFiles ? { ...rule, excludes: [...rule.excludes, ...emittedPaths] } : rule;
}

function normalizeRequired(rule, source, modulePath) {
  const normalized = { id: rule.id, source, statement: rule.statement, enforcedBy: [...rule.enforcedBy] };
  if (rule.availableFrom) {
    normalized.availableFrom = rule.availableFrom;
  }
  if (rule.appliesTo) {
    normalized.appliesTo = rule.appliesTo.map((glob) => joinPosix(modulePath, glob));
  }
  if (rule.settings) {
    normalized.settings = rule.settings;
  }
  return normalized;
}

function normalizeBanned(entry, source) {
  return { id: entry.id, source, symbol: entry.symbol, fix: entry.fix };
}

/** Base rules in compiled form (globs are already repo-relative). */
export function compileBase(base) {
  return {
    owners: [...base.owners],
    required: base.required.map((rule) => normalizeRequired(rule, RULE_SOURCE.base, '')),
    forbidden: base.forbidden.map((rule) => withEmittedExcludes(normalizeForbidden(rule, RULE_SOURCE.base, ''), [PATHS.globalconfig])),
    bannedSymbols: base.bannedSymbols.map((entry) => normalizeBanned(entry, RULE_SOURCE.base)),
    thresholds: { ...base.thresholds },
    analyzers: { ...base.analyzers },
    recipes: [...(base.recipes ?? [])],
  };
}

/**
 * Compiled module = compiled base (minus exemptions) + the module's own rules.
 * @param {object} compiledBase result of compileBase
 * @param {object} moduleRules parsed module.rules.yaml
 * @param {{ path: string, rulesFile: string, kind: string }} location
 */
export function compileModule(compiledBase, moduleRules, location) {
  const exempt = new Set((moduleRules.exemptions ?? []).map((exemption) => exemption.rule));
  const inherited = (rules) => rules.filter((rule) => !exempt.has(rule.id));
  const own = (list, normalize) => (moduleRules[list] ?? []).map((rule) => normalize(rule, RULE_SOURCE.module, location.path));

  return {
    module: moduleRules.module,
    kind: location.kind,
    path: location.path,
    rulesFile: location.rulesFile,
    description: moduleRules.description ?? '',
    owners: [...(moduleRules.owners ?? compiledBase.owners)],
    mayDependOn: [...(moduleRules.mayDependOn ?? [])],
    mustNotDependOn: [...(moduleRules.mustNotDependOn ?? [])],
    required: [...inherited(compiledBase.required), ...own('required', normalizeRequired)],
    forbidden: [...inherited(compiledBase.forbidden), ...own('forbidden', normalizeForbidden)]
      .map((rule) => withEmittedExcludes(rule, [joinPosix(location.path, MODULE_FILES.editorconfig)])),
    bannedSymbols: [...inherited(compiledBase.bannedSymbols), ...own('bannedSymbols', normalizeBanned)],
    thresholds: { ...compiledBase.thresholds, ...(moduleRules.thresholds ?? {}) },
    analyzers: { ...compiledBase.analyzers, ...(moduleRules.analyzers ?? {}) },
    recipes: [...new Set([...compiledBase.recipes, ...(moduleRules.recipes ?? [])])],
    exemptions: (moduleRules.exemptions ?? []).map((exemption) => ({ ...exemption })),
  };
}
