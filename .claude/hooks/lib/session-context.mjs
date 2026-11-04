// The SessionStart context: how the guardrails work and the current state of the compiled rules.
import { loadCompiledRules } from './compiled-rules.mjs';
import { EnvVars, NewModuleSkill, PlatformOwnedGlobs, PlatformOwnerEnabledValue, RepoPaths } from './constants.mjs';

function compiledRulesLine(loaded) {
  if (loaded.status === 'missing') return `${RepoPaths.CompiledRules} does not exist yet: hooks check paths only, not content.`;
  if (loaded.status === 'invalid') return `${RepoPaths.CompiledRules} is invalid (${loaded.error}): hooks check paths only.`;
  const names = loaded.rules.modules.map((module) => module.name);
  const modules = names.length > 0 ? names.join(', ') : 'none yet';
  return `${RepoPaths.CompiledRules}: ${loaded.rules.baseRules.length} base rule(s); modules: ${modules}.`;
}

/** The SessionStart additionalContext text. */
export function buildSessionContext(projectRoot) {
  const loaded = loadCompiledRules(projectRoot);
  const roots = loaded.rules.moduleRoots.map((root) => root.path).join(', ');
  return [
    'Codelab starter guardrails:',
    '- CI (`tools/build.ps1 -Target CI`) is the authoritative gate; the Claude hooks are an early warning (ADR-006).',
    `- ${compiledRulesLine(loaded)}`,
    `- Module folders live under: ${roots}. Each has module.rules.yaml, MODULE.md and a generated CLAUDE.md; `
      + `a new module starts with the ${NewModuleSkill} skill.`,
    `- ${PlatformOwnedGlobs.join(', ')} are platform-owned (hook unlock: ${EnvVars.PlatformOwner}=${PlatformOwnerEnabledValue}); `
      + 'generated files (rules.compiled.json, BannedSymbols.generated.txt, docs/generated, feed) are never hand-edited.',
    '- Source files change through Edit/Write only, so every change is rule-checked; shell writes into source paths are refused.',
    '- When you stop, the Stop gate runs the docs gate and the rules check on changed modules.',
  ].join('\n');
}
