// What `verify-vectors.mjs --prefix <P>` runs. Pure functions (argument parsing, toolchain resolution, the step list)
// so the tests can check them without starting dotnet or vitest.
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { UsageError } from '../../extract/lib/cli-args.mjs';

/** The prefix the checked-in vectors are pinned to. */
export const PINNED_PREFIX = 'CODELABTEST';
/** The grammar both runtimes accept (Codelab.Security's is the stricter: 2-16 characters). */
export const PREFIX_SHAPE = /^[A-Z][A-Z0-9]{1,15}$/;

export const Variables = Object.freeze({
  dotnet: 'CODELAB_DOTNET',
  regenerate: 'CODELAB_REGEN_VECTORS',
  output: 'CODELAB_VECTORS_OUT',
  generatedPrefix: 'CODELAB_VECTOR_PREFIX',
  tsDirectory: 'CODELAB_VECTORS_DIR',
  tsPrefix: 'CODELAB_VECTOR_PREFIX',
});

/** Mirrors tools/build.ps1 Resolve-Dotnet: the override, then the private SDK when present, then PATH. */
export const PRIVATE_DOTNET = 'E:\\codelab-tools\\dotnet10\\dotnet.exe';
const PATH_DOTNET = 'dotnet';

export const RepoPaths = Object.freeze({
  vectors: 'packages/vectors',
  securityTests: 'packages/dotnet/Codelab.Security.Tests/Codelab.Security.Tests.csproj',
  csharpRunner: 'packages/dotnet/Codelab.Security.Tests/Vectors',
  ngSecurity: 'packages/npm/ng-security',
  tsRunner: 'packages/npm/ng-security/src/vectors',
});

/** The vector files every set must contain. */
export const VECTOR_FILES = Object.freeze([
  'canonical.vectors.json',
  'session-keys.vectors.json',
  'request-signature.vectors.json',
  'response-signature.vectors.json',
  'public-id.vectors.json',
]);

const TEST_FILTER = 'Category=GoldenVectors';
const VITEST_ENTRY = join('node_modules', 'vitest', 'vitest.mjs');
const TS_SPEC_FOLDER = 'src/vectors';
const OFF = '';
const KNOWN_OPTIONS = new Set(['prefix', 'keep']);

/**
 * Parses the CLI options ({ options, positionals } from parseArgs) into { prefix, keep }.
 * @throws UsageError on a missing or malformed prefix, a positional argument or an unknown option
 */
export function parseVerifyOptions({ options, positionals }) {
  if (positionals.length > 0) throw new UsageError(`unexpected argument "${positionals[0]}"`);
  const unknown = Object.keys(options).find((name) => !KNOWN_OPTIONS.has(name));
  if (unknown) throw new UsageError(`unknown option --${unknown}`);
  const prefix = options.prefix;
  if (typeof prefix !== 'string') throw new UsageError('missing required option --prefix <PREFIX>');
  if (!PREFIX_SHAPE.test(prefix)) {
    throw new UsageError(`--prefix "${prefix}" must be 2-16 characters: an upper-case letter, then upper-case letters or digits`);
  }
  if (options.keep !== undefined && options.keep !== true) throw new UsageError('--keep takes no value');
  return { prefix, keep: options.keep === true };
}

/** The dotnet executable to run. */
export function resolveDotnet(env, exists = existsSync) {
  if (env[Variables.dotnet]) return env[Variables.dotnet];
  return exists(PRIVATE_DOTNET) ? PRIVATE_DOTNET : PATH_DOTNET;
}

/** The vitest entry script, searched upwards from the ng-security folder (npm workspaces hoist it). */
export function findVitestEntry(startDirectory, exists = existsSync) {
  for (let directory = startDirectory; ; directory = dirname(directory)) {
    const candidate = join(directory, VITEST_ENTRY);
    if (exists(candidate)) return candidate;
    if (dirname(directory) === directory) return null;
  }
}

/** The entry points a run needs that are absent (each one a MISSING failure, never a skip). */
export function missingEntryPoints(repoRoot, exists = existsSync) {
  const required = [RepoPaths.securityTests, RepoPaths.csharpRunner, RepoPaths.tsRunner, join(RepoPaths.vectors, 'fixture-keys')];
  return required.filter((path) => !exists(join(repoRoot, path)));
}

/**
 * The steps of a run. The pinned prefix checks both runners against packages/vectors; any other prefix has the C#
 * generator write a set into outputDirectory and the TypeScript runner verify that set.
 */
export function planSteps({ prefix, repoRoot, outputDirectory, dotnet, node, vitestEntry }) {
  const pinned = prefix === PINNED_PREFIX;
  const vectorDirectory = pinned ? join(repoRoot, RepoPaths.vectors) : outputDirectory;
  const generatorEnv = pinned
    ? { [Variables.regenerate]: OFF, [Variables.output]: OFF, [Variables.generatedPrefix]: OFF }
    : { [Variables.regenerate]: OFF, [Variables.output]: outputDirectory, [Variables.generatedPrefix]: prefix };
  return [
    {
      name: pinned ? `C# runner against ${RepoPaths.vectors}` : `C# generator and runner for prefix ${prefix}`,
      command: dotnet,
      args: ['test', join(repoRoot, RepoPaths.securityTests), '--filter', TEST_FILTER, '-nologo'],
      cwd: repoRoot,
      env: generatorEnv,
      expectsFiles: pinned ? null : outputDirectory,
    },
    {
      name: `TypeScript runner against ${pinned ? RepoPaths.vectors : 'the generated set'} (prefix ${prefix})`,
      command: node,
      args: [vitestEntry, 'run', TS_SPEC_FOLDER],
      cwd: join(repoRoot, RepoPaths.ngSecurity),
      env: { [Variables.tsDirectory]: vectorDirectory, [Variables.tsPrefix]: prefix },
      expectsFiles: null,
    },
  ];
}

/** The vector files a generated set is missing. */
export function missingVectorFiles(directory, exists = existsSync) {
  return VECTOR_FILES.filter((file) => !exists(join(directory, file)));
}
