// Glob matching for repo-relative, forward-slash paths.
// Supported: "**" (any number of path segments), "*" (within a segment), "?" (one char), "{a,b}" alternation.
// A pattern without "/" matches the file NAME at any depth (gitignore style), so "*Foo*" matches the name anywhere,
// unless the caller asks for { anchored: true }, which always matches from the root.
const SEGMENT_SEPARATOR = '/';
const REGEX_SPECIALS = /[.+^$()|[\]\\]/;
const compiledCache = new Map();

/**
 * Compiles a glob to a RegExp anchored on the whole path.
 * @param {string} pattern glob, forward slashes
 * @param {{ caseInsensitive?: boolean, anchored?: boolean }} [options]
 */
export function globToRegExp(pattern, { caseInsensitive = false, anchored = false } = {}) {
  const cacheKey = `${caseInsensitive ? 'i' : 's'}${anchored ? 'a' : 'n'}:${pattern}`;
  const cached = compiledCache.get(cacheKey);
  if (cached) return cached;
  const effective = anchored || pattern.includes(SEGMENT_SEPARATOR) ? pattern : `**/${pattern}`;
  const compiled = new RegExp(`^${translate(effective)}$`, caseInsensitive ? 'i' : '');
  compiledCache.set(cacheKey, compiled);
  return compiled;
}

function translate(glob) {
  let source = '';
  let braceDepth = 0;
  for (let index = 0; index < glob.length; index += 1) {
    const char = glob[index];
    if (char === '*' && glob[index + 1] === '*') {
      const followedBySlash = glob[index + 2] === SEGMENT_SEPARATOR;
      source += followedBySlash ? '(?:.*/)?' : '.*';
      index += followedBySlash ? 2 : 1;
    } else if (char === '*') {
      source += '[^/]*';
    } else if (char === '?') {
      source += '[^/]';
    } else if (char === '{') {
      braceDepth += 1;
      source += '(?:';
    } else if (char === '}' && braceDepth > 0) {
      braceDepth -= 1;
      source += ')';
    } else if (char === ',' && braceDepth > 0) {
      source += '|';
    } else {
      source += REGEX_SPECIALS.test(char) ? `\\${char}` : char;
    }
  }
  return source;
}

/** Normalises a path to forward slashes without a leading "./". */
export function toPosixPath(path) {
  return path.replaceAll('\\', SEGMENT_SEPARATOR).replace(/^\.\//, '');
}

/** True when the path matches the glob. */
export function matchesGlob(path, pattern, options) {
  return globToRegExp(pattern, options).test(toPosixPath(path));
}
