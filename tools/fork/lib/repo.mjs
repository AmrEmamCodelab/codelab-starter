// Repository operations shared by init, remove-module and upstream-merge.
import { existsSync } from 'node:fs';
import path from 'node:path';
import { RULES_COMPILE_SCRIPT } from './constants.mjs';
import { computeHashes } from './hashing.mjs';
import { git, gitList, gitText, node } from './process.mjs';

/** Raised when a precondition fails; the CLI maps it to ExitCode.failed with nothing changed. */
export class RefusalError extends Error {}

/** True when `root` is the top of a git work tree. */
export function isGitRoot(root) {
  const result = git(root, ['rev-parse', '--show-toplevel'], { check: false });
  return result.code === 0 && path.resolve(result.stdout.trim()) === path.resolve(root);
}

/** Tracked changes (staged or not); untracked files are ignored. */
export function trackedChanges(root) {
  return gitList(root, ['status', '--porcelain=v1', '-z', '--untracked-files=no']);
}

/** Refuses unless `root` is a git work tree root without tracked changes. */
export function assertCleanGitRoot(root) {
  if (!isGitRoot(root)) throw new RefusalError(`${root} is not the root of a git work tree.`);
  const changes = trackedChanges(root);
  if (changes.length > 0) {
    throw new RefusalError(`The work tree has uncommitted changes; commit or stash them first:\n  ${changes.join('\n  ')}`);
  }
}

/** Files in the index ('/' separators). */
export function trackedFiles(root) {
  return gitList(root, ['ls-files', '-z']);
}

/** Tracked plus untracked, non-ignored files (the working tree as git would commit it). */
export function workingTreeFiles(root) {
  return gitList(root, ['ls-files', '-z', '--cached', '--others', '--exclude-standard']).filter((file) => existsSync(path.join(root, file)));
}

/** The commit id of a revision. */
export function resolveCommit(root, revision) {
  return gitText(root, ['rev-parse', '--verify', `${revision}^{commit}`]);
}

/** Recomputes the manifest hashes over the index. */
export function hashTrackedFiles(root) {
  return computeHashes(root, trackedFiles(root));
}

/** Files that are untracked and not ignored. */
function untrackedFiles(root) {
  return gitList(root, ['ls-files', '-z', '--others', '--exclude-standard']);
}

/** Reruns the rules compiler (node tools/rules-engine/compile.mjs); throws when it is missing or fails. */
export function compileRules(root) {
  const script = path.join(root, RULES_COMPILE_SCRIPT);
  if (!existsSync(script)) throw new RefusalError(`${RULES_COMPILE_SCRIPT} not found; the rules outputs cannot be regenerated.`);
  node(root, script, [], { inherit: true });
}

/**
 * Reruns the rules compiler and stages what it changed: modified tracked files and files it created. Call it only
 * on a tree whose other changes are all staged (upstream-merge), because it stages every tracked modification.
 */
export function compileRulesAndStage(root) {
  const before = new Set(untrackedFiles(root));
  compileRules(root);
  const created = untrackedFiles(root).filter((file) => !before.has(file));
  git(root, ['add', '-u']);
  if (created.length > 0) git(root, ['add', '--', ...created]);
}
