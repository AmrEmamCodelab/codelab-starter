#!/usr/bin/env node
// Resolves the git ref that CI diffs against for "what changed on this branch" checks (the docs gate's DOCS005
// module-changed check, and the content check's protected-path report).
//
// Usage: node tools/ci/base-ref.mjs [--root <path>] [--base <ref>]
//   prints the resolved ref on stdout and exits 0;
//   prints NONE and exits 0 when the repository has no commit yet (nothing is committed, so the working tree
//   compared with HEAD is the whole change);
//   exits 1 when an explicit ref (--base or CODELAB_BASE_REF) or every default ref cannot be resolved. CI must
//   never fall back to comparing HEAD with itself, because then a committed module change is invisible.
//
// Resolution order: --base, then $CODELAB_BASE_REF, then the first of DEFAULT_BASE_REFS that exists.

import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const EXIT_RESOLVED = 0;
export const EXIT_UNRESOLVED = 1;
export const EXIT_USAGE = 2;

export const BASE_REF_ENV_VAR = 'CODELAB_BASE_REF';
export const DEFAULT_BASE_REFS = Object.freeze(['origin/main', 'main']);
/** Printed when the repository has no commit at all. */
export const NO_BASE = 'NONE';

const ROOT_OPTION = '--root';
const BASE_OPTION = '--base';
const DEFAULT_ROOT = '.';
const GIT_SUCCESS = 0;
const HEAD = 'HEAD';

function gitSucceeds(root, args) {
  const result = spawnSync('git', args, { cwd: root, encoding: 'utf8', windowsHide: true });
  return result.status === GIT_SUCCESS;
}

function refExists(root, ref) {
  return gitSucceeds(root, ['rev-parse', '--verify', '--quiet', `${ref}^{commit}`]);
}

/**
 * The base ref to diff against.
 * @param {{ root: string, explicitRef?: string | null, env?: NodeJS.ProcessEnv }} options
 * @returns {{ ref: string | null, error: string | null }} ref is NO_BASE for a repository without commits
 */
export function resolveBaseRef({ root, explicitRef = null, env = process.env }) {
  const requested = explicitRef || env[BASE_REF_ENV_VAR] || null;
  if (requested) {
    return refExists(root, requested)
      ? { ref: requested, error: null }
      : { ref: null, error: `base ref '${requested}' does not resolve to a commit (fetch it, or fix ${BASE_REF_ENV_VAR}/--base).` };
  }
  if (!refExists(root, HEAD)) return { ref: NO_BASE, error: null };
  const found = DEFAULT_BASE_REFS.find((ref) => refExists(root, ref));
  return found
    ? { ref: found, error: null }
    : { ref: null, error: `none of ${DEFAULT_BASE_REFS.join(', ')} exists; pass --base <ref> or set ${BASE_REF_ENV_VAR}.` };
}

/** Parses argv into { root, base }; returns null for an unknown or incomplete argument. */
export function parseArguments(argv) {
  const options = { root: DEFAULT_ROOT, base: null };
  for (let index = 0; index < argv.length; index += 2) {
    const value = argv[index + 1];
    if (value === undefined) return null;
    if (argv[index] === ROOT_OPTION) options.root = value;
    else if (argv[index] === BASE_OPTION) options.base = value;
    else return null;
  }
  return options;
}

export function main(argv) {
  const options = parseArguments(argv);
  if (!options) {
    console.error(`Usage: node tools/ci/base-ref.mjs [${ROOT_OPTION} <path>] [${BASE_OPTION} <ref>]`);
    return EXIT_USAGE;
  }
  const { ref, error } = resolveBaseRef({ root: path.resolve(options.root), explicitRef: options.base });
  if (error) {
    console.error(`base ref: ${error}`);
    return EXIT_UNRESOLVED;
  }
  console.log(ref);
  return EXIT_RESOLVED;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main(process.argv.slice(2));
}
