// Path helpers: every path the hooks compare is repo-relative with forward slashes.
import path from 'node:path';
import { EnvVars } from './constants.mjs';

const GIT_BASH_DRIVE = /^\/([a-zA-Z])(\/|$)/;
const PARENT_PREFIX = '..';

/** The project root: CLAUDE_PROJECT_DIR (set by Claude Code) or the hook input's cwd. */
export function resolveProjectRoot(input, env = process.env) {
  return path.resolve(env[EnvVars.ProjectDir] || input?.cwd || process.cwd());
}

/** Converts Git Bash style `/e/dir` to `E:/dir` on Windows; other paths pass through. */
export function fromGitBashPath(rawPath) {
  if (process.platform !== 'win32') return rawPath;
  return rawPath.replace(GIT_BASH_DRIVE, (_match, drive, slash) => `${drive.toUpperCase()}:${slash || '/'}`);
}

/** Absolute path for a path written relative to `baseDir` (or already absolute). */
export function toAbsolute(rawPath, baseDir) {
  return path.resolve(baseDir, fromGitBashPath(rawPath));
}

/**
 * Repo-relative forward-slash path, or null when the path is outside the project root.
 * Comparison is case-insensitive on Windows.
 */
export function toRepoRelative(absolutePath, projectRoot) {
  const normalizedRoot = path.resolve(projectRoot);
  const normalizedPath = path.resolve(absolutePath);
  const relative = path.relative(normalizedRoot, normalizedPath);
  if (relative === '') return '';
  if (relative.startsWith(PARENT_PREFIX) || path.isAbsolute(relative)) return null;
  return relative.split(path.sep).join('/');
}
