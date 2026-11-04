// The rules engine: load + validate every rules file, apply inheritance, and produce all generated outputs in memory.
// Writing and drift-checking are separate steps so --check never touches the disk.
import { existsSync, readdirSync } from 'node:fs';
import path from 'node:path';
import {
  BASE_RULE_PREFIX, CSPROJ_EXTENSION, EMITTED_MODULE_FILE_NAMES, GENERATED_BY, GENERATED_MARKER, JSON_INDENT,
  MODULE_FILES, MODULE_KINDS, NEWLINE, PATHS, SCHEMA_VERSION,
} from './constants.mjs';
import { ProblemList } from './errors.mjs';
import {
  findFilesNamed, findModuleRuleFiles, fromPosixRelative, joinPosix, normalizeNewlines, readText, readTextIfExists,
  toPosixRelative, writeText,
} from './files.mjs';
import { createValidators, loadRulesFile } from './load.mjs';
import { compileBase, compileModule } from './merge.mjs';
import {
  checkBannedSymbols, checkDependencies, checkExemptions, checkPatterns, checkRecipes, checkRuleIds, checkUniqueModuleNames,
  declaredRuleIds, moduleRulePrefix, parseBannedSymbolList, resolveModuleKind,
} from './validate.mjs';
import { emitBannedSymbols } from './emitters/banned-symbols.mjs';
import { emitClaudeMd } from './emitters/claude-md.mjs';
import { emitCodeMetrics } from './emitters/code-metrics.mjs';
import { emitEditorconfig } from './emitters/editorconfig.mjs';
import { emitEslintBoundaries } from './emitters/eslint-boundaries.mjs';
import { emitGlobalconfig } from './emitters/globalconfig.mjs';

const DRIFT = Object.freeze({ missing: 'missing', changed: 'out of date', orphan: 'orphaned (no module.rules.yaml any more)' });

function folderHasCsproj(absoluteDir) {
  return readdirSync(absoluteDir).some((name) => name.endsWith(CSPROJ_EXTENSION));
}

function loadBase(root, validators, problems) {
  const base = loadRulesFile(fromPosixRelative(root, PATHS.baseRules), PATHS.baseRules, validators.base, problems);
  if (base) {
    checkRuleIds(base, BASE_RULE_PREFIX, PATHS.baseRules, problems);
    checkPatterns(base, PATHS.baseRules, problems);
    checkRecipes(base, (recipe) => existsSync(fromPosixRelative(root, recipe)), PATHS.baseRules, problems);
  }
  return base;
}

function loadModule(root, absoluteFile, context) {
  const { validators, problems, baseIds, globalBanned, compiledBase } = context;
  const rulesFile = toPosixRelative(root, absoluteFile);
  const moduleRules = loadRulesFile(absoluteFile, rulesFile, validators.module, problems);
  if (!moduleRules) {
    return undefined;
  }
  checkRuleIds(moduleRules, moduleRulePrefix(moduleRules.module), rulesFile, problems);
  checkPatterns(moduleRules, rulesFile, problems);
  checkRecipes(moduleRules, (recipe) => existsSync(fromPosixRelative(root, recipe)), rulesFile, problems);
  checkExemptions(moduleRules, baseIds, rulesFile, problems);
  checkDependencies(moduleRules, rulesFile, problems);
  const absoluteDir = path.dirname(absoluteFile);
  const kind = resolveModuleKind(moduleRules, folderHasCsproj(absoluteDir), rulesFile, problems);
  const compiled = compileModule(compiledBase, moduleRules, { path: toPosixRelative(root, absoluteDir), rulesFile, kind });
  checkBannedSymbols(compiled.bannedSymbols, globalBanned, rulesFile, problems);
  return compiled;
}

/** Repo-relative output files for one module. */
function moduleOutputs(module) {
  const at = (fileName) => joinPosix(module.path, fileName);
  const outputs = [{ path: at(MODULE_FILES.claudeMd), content: emitClaudeMd(module) }];
  if (module.kind === MODULE_KINDS.dotnet) {
    outputs.push(
      { path: at(MODULE_FILES.bannedSymbols), content: emitBannedSymbols(module) },
      { path: at(MODULE_FILES.editorconfig), content: emitEditorconfig(module) },
      { path: at(MODULE_FILES.codeMetrics), content: emitCodeMetrics(module) },
    );
  }
  return outputs;
}

function compiledDocument(compiledBase, modules) {
  return {
    $comment: `${GENERATED_MARKER} from rules/base.rules.yaml and every **/module.rules.yaml. Do not edit.`,
    generatedBy: GENERATED_BY,
    schemaVersion: SCHEMA_VERSION,
    base: compiledBase,
    modules,
  };
}

/**
 * Compiles every rules file under `root`.
 * @param {string} root absolute repo root
 * @returns {{ compiled: object, outputs: { path: string, content: string }[] }}
 * @throws {import('./errors.mjs').RulesError} listing every problem found
 */
export function compileRepository(root) {
  const problems = new ProblemList();
  const validators = createValidators(readText(fromPosixRelative(root, PATHS.schema)));
  const base = loadBase(root, validators, problems);
  problems.throwIfAny();

  const compiledBase = compileBase(base);
  const globalBanned = parseBannedSymbolList(readTextIfExists(fromPosixRelative(root, PATHS.sdkGlobalBannedSymbols)));
  checkBannedSymbols(compiledBase.bannedSymbols, globalBanned, PATHS.baseRules, problems);
  const context = { validators, problems, baseIds: new Set(declaredRuleIds(base)), globalBanned, compiledBase };
  const modules = findModuleRuleFiles(root)
    .map((file) => loadModule(root, file, context))
    .filter(Boolean);
  checkUniqueModuleNames(modules, problems);
  problems.throwIfAny();

  const compiled = compiledDocument(compiledBase, modules);
  const outputs = [
    { path: PATHS.compiled, content: JSON.stringify(compiled, null, JSON_INDENT) + NEWLINE },
    { path: PATHS.eslintBoundaries, content: emitEslintBoundaries(compiled) },
    { path: PATHS.globalconfig, content: emitGlobalconfig(compiledBase) },
    ...modules.flatMap(moduleOutputs),
  ];
  return { compiled, outputs };
}

/** Writes outputs whose content changed. Returns the repo-relative paths written. */
export function writeOutputs(root, outputs) {
  const written = [];
  for (const output of outputs) {
    const absolute = fromPosixRelative(root, output.path);
    const current = readTextIfExists(absolute);
    if (current === undefined || normalizeNewlines(current) !== output.content) {
      writeText(absolute, output.content);
      written.push(output.path);
    }
  }
  return written;
}

/** Generated module files (carrying the marker) that no current module produces. */
function findOrphans(root, outputs) {
  const expected = new Set(outputs.map((output) => output.path));
  return findFilesNamed(root, EMITTED_MODULE_FILE_NAMES)
    .map((absolute) => toPosixRelative(root, absolute))
    .filter((relative) => !expected.has(relative))
    .filter((relative) => readText(fromPosixRelative(root, relative)).includes(GENERATED_MARKER));
}

/**
 * Compares outputs with the files on disk (line endings ignored).
 * @returns {{ path: string, reason: string }[]} empty when nothing drifted
 */
export function findDrift(root, outputs) {
  const drift = [];
  for (const output of outputs) {
    const current = readTextIfExists(fromPosixRelative(root, output.path));
    if (current === undefined) {
      drift.push({ path: output.path, reason: DRIFT.missing });
    } else if (normalizeNewlines(current) !== output.content) {
      drift.push({ path: output.path, reason: DRIFT.changed });
    }
  }
  for (const orphan of findOrphans(root, outputs)) {
    drift.push({ path: orphan, reason: DRIFT.orphan });
  }
  return drift;
}

/** True when `root` looks like a repo the engine can compile. */
export function hasRulesSources(root) {
  return existsSync(fromPosixRelative(root, PATHS.schema)) && existsSync(fromPosixRelative(root, PATHS.baseRules));
}
