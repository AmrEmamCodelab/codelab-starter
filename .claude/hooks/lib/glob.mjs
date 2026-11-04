// Minimal glob matcher for repo-relative, forward-slash paths. Supported: `**`, `*`, `?`, `{a,b}`.
// A pattern without '/' matches the file name at any depth (gitignore style): `*.cs` matches `a/b/C.cs`.
// Matching is case-insensitive because the starter targets Windows file systems.

const GLOBSTAR_SEGMENT = '(?:.*/)?';
const ANY_SEQUENCE = '.*';
const ANY_IN_SEGMENT = '[^/]*';
const ONE_IN_SEGMENT = '[^/]';
const REGEX_SPECIALS = /[.+^${}()|[\]\\]/g;

const cache = new Map();

function escapeLiteral(text) {
  return text.replace(REGEX_SPECIALS, '\\$&');
}

function translateStar(glob, index) {
  const isDouble = glob[index + 1] === '*';
  if (!isDouble) return { regex: ANY_IN_SEGMENT, consumed: 1 };
  const atSegmentStart = index === 0 || glob[index - 1] === '/';
  const followedBySlash = glob[index + 2] === '/';
  if (atSegmentStart && followedBySlash) return { regex: GLOBSTAR_SEGMENT, consumed: 3 };
  return { regex: ANY_SEQUENCE, consumed: 2 };
}

function translateBraces(glob, index) {
  const end = glob.indexOf('}', index);
  if (end < 0) return { regex: escapeLiteral('{'), consumed: 1 };
  const alternatives = glob.slice(index + 1, end).split(',').map((alt) => translate(alt));
  return { regex: `(?:${alternatives.join('|')})`, consumed: end - index + 1 };
}

function translate(glob) {
  let regex = '';
  let index = 0;
  while (index < glob.length) {
    const char = glob[index];
    let step;
    if (char === '*') step = translateStar(glob, index);
    else if (char === '?') step = { regex: ONE_IN_SEGMENT, consumed: 1 };
    else if (char === '{') step = translateBraces(glob, index);
    else step = { regex: escapeLiteral(char), consumed: 1 };
    regex += step.regex;
    index += step.consumed;
  }
  return regex;
}

/** Compiles a glob to an anchored, case-insensitive RegExp (cached). */
export function globToRegExp(glob) {
  if (cache.has(glob)) return cache.get(glob);
  const anchoredAnywhere = !glob.includes('/');
  const body = translate(glob.replace(/^\.?\//, ''));
  const regex = new RegExp(`^${anchoredAnywhere ? GLOBSTAR_SEGMENT : ''}${body}$`, 'i');
  cache.set(glob, regex);
  return regex;
}

/** True when the repo-relative path matches the glob. */
export function matchesGlob(relativePath, glob) {
  return globToRegExp(glob).test(relativePath);
}

/** True when the path matches at least one glob of the list (an empty or missing list matches nothing). */
export function matchesAnyGlob(relativePath, globs) {
  return (globs ?? []).some((glob) => matchesGlob(relativePath, glob));
}
