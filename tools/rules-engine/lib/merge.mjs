// Inheritance: every module gets the base rules (minus its exemptions) plus its own rules.
import { RULE_SOURCE, SEVERITY } from './constants.mjs';
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
    fix: rule.fix,
    severity: rule.severity ?? SEVERITY.error,
  };
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
    forbidden: base.forbidden.map((rule) => normalizeForbidden(rule, RULE_SOURCE.base, '')),
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
    forbidden: [...inherited(compiledBase.forbidden), ...own('forbidden', normalizeForbidden)],
    bannedSymbols: [...inherited(compiledBase.bannedSymbols), ...own('bannedSymbols', normalizeBanned)],
    thresholds: { ...compiledBase.thresholds, ...(moduleRules.thresholds ?? {}) },
    analyzers: { ...compiledBase.analyzers, ...(moduleRules.analyzers ?? {}) },
    recipes: [...new Set([...compiledBase.recipes, ...(moduleRules.recipes ?? [])])],
    exemptions: (moduleRules.exemptions ?? []).map((exemption) => ({ ...exemption })),
  };
}
