// Decides whether an inline code span names a repository path, and checks that it resolves.
import { RuleIds, PATH_SEPARATOR } from './constants.mjs';
import { globMatches, referenceResolves } from './repo-index.mjs';

const BACKSLASH = '\\';
const GLOB_CHARACTERS = /[*?]/;
/** <slug>, {name}, [id], ..., $var, %VAR%: a placeholder, never a literal path segment. */
const PLACEHOLDER = /<[^>]*>|\{[^}]*\}|\[[^\]]*\]|\.\.\.|\$|%/;
/** Text that is never a path: URLs, scoped packages, config keys, routes, options, expressions, Windows drives. */
const NOT_A_PATH = /^(?:[@#~\-/]|\.?$)|:|\s|[=(),;"'!|&]/;
const LEADING_CURRENT_DIRECTORY = /^\.\//;
const DOT_NAME = /^\.[A-Za-z][\w.-]*$/;
/** "Directory.Build.props/.targets", "permissions.json/.md": a shorthand for a sibling with another extension. */
const ALTERNATE_EXTENSION = /(?<=[^/])\/\.[A-Za-z0-9]+$/;
const EXTENSION = /\.([A-Za-z0-9]+)$/;

/**
 * @typedef {{ path: string, directoryOnly: boolean, glob: boolean, placeholderPrefix: string | null }} PathCandidate
 * placeholderPrefix: for "docs/features/<slug>.md", the folders before the first placeholder segment ("docs/features").
 */

function hasKnownExtension(segment, extensions) {
  const match = EXTENSION.exec(segment);
  return match !== null && extensions.has(match[1].toLowerCase());
}

/**
 * Returns the path a code span names, or null when the span is not a path.
 * A span is a path when it has no spaces or URL/key syntax and either ends with a known file extension, ends with
 * "/", or starts with a folder that exists at the repository root ("frontend/starter-web"). A lone ".name" is an
 * extension or CSS class, never a path.
 * @param {string} content code span text
 * @param {{ extensions: Set<string>, topLevelEntries: Set<string> }} options
 * @returns {PathCandidate | null}
 */
export function classifyPathCandidate(content, { extensions, topLevelEntries }) {
  const text = content.trim().replaceAll(BACKSLASH, PATH_SEPARATOR).replace(LEADING_CURRENT_DIRECTORY, '')
    .replace(ALTERNATE_EXTENSION, '');
  // ".ts", ".scss", ".Tests", ".shell__brand": an extension, suffix or CSS class named on its own, never a file.
  if (NOT_A_PATH.test(text) || DOT_NAME.test(text)) return null;
  const segments = text.split(PATH_SEPARATOR);
  const directoryOnly = text.endsWith(PATH_SEPARATOR);
  const last = directoryOnly ? segments[segments.length - 2] : segments[segments.length - 1];
  const nested = segments.length > 1;
  const looksLikePath = hasKnownExtension(last, extensions)
    || (nested && directoryOnly)
    || (nested && topLevelEntries.has(segments[0]));
  if (!looksLikePath) return null;
  const placeholderAt = segments.findIndex((segment) => PLACEHOLDER.test(segment));
  const placeholderPrefix = placeholderAt === -1 ? null : segments.slice(0, placeholderAt).join(PATH_SEPARATOR);
  return { path: text, directoryOnly, glob: GLOB_CHARACTERS.test(text), placeholderPrefix };
}

/**
 * Checks a path candidate. Plain paths resolve root-relative, document-relative or as the tail of an existing path;
 * globs must match something; a placeholder path only needs the folders before its placeholder to exist.
 * @returns {{ ruleId: string, message: string } | null} the problem, or null when it resolves
 */
export function checkPathCandidate(index, candidate, { documentDirectory }) {
  const context = { documentDirectory };
  if (candidate.placeholderPrefix !== null) {
    if (candidate.placeholderPrefix === '' || GLOB_CHARACTERS.test(candidate.placeholderPrefix)) return null;
    return referenceResolves(index, candidate.placeholderPrefix, { ...context, directoryOnly: true })
      ? null
      : { ruleId: RuleIds.pathMissing, message: `folder '${candidate.placeholderPrefix}' (before the placeholder in '${candidate.path}') does not exist` };
  }
  if (candidate.glob) {
    return globMatches(index, candidate.path, context)
      ? null
      : { ruleId: RuleIds.globEmpty, message: `glob '${candidate.path}' matches no file or folder` };
  }
  return referenceResolves(index, candidate.path, { ...context, directoryOnly: candidate.directoryOnly })
    ? null
    : {
      ruleId: RuleIds.pathMissing,
      message: `'${candidate.path}' does not resolve: no ${candidate.directoryOnly ? 'folder' : 'file or folder'} at that path (root-relative, document-relative or as a path tail; case-sensitive)`,
    };
}

/** The names at the repository root (files and folders), used to recognise "tools/x" as a path. */
export function topLevelEntriesOf(index) {
  const names = new Set();
  for (const entry of [...index.fileSet, ...index.directorySet]) names.add(entry.split(PATH_SEPARATOR)[0]);
  return names;
}
