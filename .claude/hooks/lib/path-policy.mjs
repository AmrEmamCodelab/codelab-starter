// Path-level policies that do not depend on file content: generated files, platform-owned paths, new modules
// without a rules file, and whether a shell write target is a source path.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { matchesAnyGlob } from './glob.mjs';
import {
  EnvVars,
  GeneratedMarkers,
  GeneratedMarkerScanChars,
  HookRuleIds,
  ModuleRulesExemptionsKey,
  ModuleScaffoldFiles,
  NewModuleSkill,
  NullDevices,
  PlatformOwnedGlobs,
  PlatformOwnerEnabledValue,
  StopGateTimeouts,
} from './constants.mjs';

const GIT_CHECK_IGNORE_IGNORED = 0;

function readHead(absolutePath) {
  try {
    const fd = fs.openSync(absolutePath, 'r');
    try {
      const buffer = Buffer.alloc(GeneratedMarkerScanChars);
      const bytes = fs.readSync(fd, buffer, 0, GeneratedMarkerScanChars, 0);
      return buffer.subarray(0, bytes).toString('utf8');
    } finally {
      fs.closeSync(fd);
    }
  } catch {
    return '';
  }
}

/** A refusal: { ruleId, reason }. */
function refusal(ruleId, reason) {
  return { ruleId, reason };
}

/** Refuses edits to generated outputs (by glob, or by a generated marker at the top of an existing file). */
export function checkGenerated({ relativePath, absolutePath, rules }) {
  const byGlob = matchesAnyGlob(relativePath, rules.generatedGlobs);
  const head = byGlob ? '' : readHead(absolutePath);
  const byMarker = GeneratedMarkers.some((marker) => head.includes(marker));
  if (!byGlob && !byMarker) return null;
  return refusal(
    HookRuleIds.GeneratedFile,
    `${relativePath} is generated and must not be edited by hand. Fix: change its source (the *.rules.yaml file, `
      + 'the code the doc is generated from, or the package) and rerun the generator, '
      + 'e.g. `node tools/rules-engine/compile.mjs` or `tools/build.ps1 -Pack`.',
  );
}

function isPlatformOwner(env) {
  return env[EnvVars.PlatformOwner] === PlatformOwnerEnabledValue;
}

/** Refuses edits to platform-owned paths (PlatformOwnedGlobs) unless CODELAB_PLATFORM_OWNER=1. */
export function checkPlatformOwner({ relativePath, env }) {
  if (!matchesAnyGlob(relativePath, PlatformOwnedGlobs)) return null;
  if (isPlatformOwner(env)) return null;
  return refusal(
    HookRuleIds.PlatformOwner,
    `${relativePath} is platform-owned (${PlatformOwnedGlobs.join(', ')}). Only the platform owner changes it. `
      + `Fix: ask the platform owner, or (if you are the owner) start Claude with ${EnvVars.PlatformOwner}=`
      + `${PlatformOwnerEnabledValue}. CODEOWNERS review still applies.`,
  );
}

const TOP_LEVEL_KEY = /^[^\s#-][^:]*:/;
const EMPTY_EXEMPTIONS = /^exemptions:\s*(?:\[\s*\])?$/;
const LINE_BREAK = /\r?\n/;

/** The top-level `exemptions` block of a module.rules.yaml (key line plus its indented lines), or '' if empty. */
export function exemptionsBlock(text) {
  const lines = (text ?? '').split(LINE_BREAK);
  const start = lines.findIndex((line) => line.startsWith(`${ModuleRulesExemptionsKey}:`));
  if (start < 0) return '';
  const block = [lines[start].trimEnd()];
  for (const line of lines.slice(start + 1)) {
    if (TOP_LEVEL_KEY.test(line)) break;
    if (line.trim() !== '') block.push(line.trimEnd());
  }
  const joined = block.join('\n');
  return EMPTY_EXEMPTIONS.test(joined) ? '' : joined;
}

/** Refuses a module.rules.yaml change that adds or alters base-rule exemptions, unless CODELAB_PLATFORM_OWNER=1. */
export function checkModuleRulesExemptions({ relativePath, before, after, env }) {
  if (relativePath.split('/').at(-1) !== ModuleScaffoldFiles.Rules || after === null) return null;
  if (isPlatformOwner(env) || exemptionsBlock(before) === exemptionsBlock(after)) return null;
  return refusal(
    HookRuleIds.PlatformOwner,
    `${relativePath}: the \`${ModuleRulesExemptionsKey}\` block drops base rules, so only the platform owner changes `
      + 'it. Fix: follow the base rule, or ask the platform owner for an exemption with a reason.',
  );
}

function isScaffoldFile(relativePath) {
  const fileName = relativePath.split('/').at(-1);
  return fileName === ModuleScaffoldFiles.Rules || fileName === ModuleScaffoldFiles.Docs;
}

/**
 * Refuses files in a module folder that has no module.rules.yaml (typically a NEW module), except the
 * module.rules.yaml and MODULE.md scaffold files themselves.
 */
export function checkModuleHasRules({ relativePath, module, projectRoot }) {
  if (!module?.root || isScaffoldFile(relativePath)) return null;
  const rulesFile = path.join(projectRoot, module.folder, ModuleScaffoldFiles.Rules);
  if (fs.existsSync(rulesFile)) return null;
  return refusal(
    HookRuleIds.ModuleWithoutRules,
    `${module.folder} has no ${ModuleScaffoldFiles.Rules}. Every module needs its rules before its code. `
      + `Fix: create the module with the \`${NewModuleSkill}\` skill (/${NewModuleSkill}), which writes `
      + `${ModuleScaffoldFiles.Rules}, ${ModuleScaffoldFiles.Docs} and the generated ${ModuleScaffoldFiles.Agent}.`,
  );
}

/** True for /dev/null, NUL, $null and friends. */
export function isNullDevice(rawTarget) {
  return NullDevices.includes(rawTarget.toLowerCase());
}

/**
 * True when git says the path is ignored (build output, node_modules, feed contents...).
 * Returns false when git is unavailable, so the path is treated as source (fail closed).
 */
export function isGitIgnored({ relativePath, projectRoot }) {
  const result = spawnSync('git', ['check-ignore', '-q', '--', relativePath], {
    cwd: projectRoot,
    timeout: StopGateTimeouts.GitMs,
    windowsHide: true,
  });
  return result.status === GIT_CHECK_IGNORE_IGNORED;
}
