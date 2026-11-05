// Finds remote resource references in built CSS and JavaScript: url(...) or @import with an http(s) or
// protocol-relative target. The package must work offline and in the no-egress zone, so the build fails on any.

const REMOTE_URL_FUNCTION = /url\(\s*(['"]?)\s*((?:https?:)?\/\/[^'")\s]+)\1\s*\)/gi;
const REMOTE_IMPORT = /@import\s+(['"])((?:https?:)?\/\/[^'"]+)\1/gi;
const TARGET_GROUP = 2;

/** Every remote URL referenced by a url() or an @import in the text, in order of appearance. */
export function findRemoteUrls(text) {
  const found = [];
  for (const pattern of [REMOTE_URL_FUNCTION, REMOTE_IMPORT]) {
    for (const match of text.matchAll(pattern)) found.push({ index: match.index, url: match[TARGET_GROUP] });
  }
  return found.sort((left, right) => left.index - right.index).map((entry) => entry.url);
}
