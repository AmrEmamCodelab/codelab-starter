/**
 * Which outgoing URLs are calls to OUR API, and what path the API sees for them.
 *
 * This is the one place the question is answered; the signer keeps no exclusion list. The server
 * decides signature exemptions from endpoint metadata alone and ignores a proof on an exempt
 * endpoint, which is what makes "sign every call to the API" safe.
 *
 * The path matters more than it looks: the server signs `Request.Path` WITHOUT `PathBase`, while the
 * browser sees an edge-proxied /api/items (the proxy strips /api) or `https://api.example.test/items` (split
 * host). Removing the configured root here is what makes both spellings sign `/items`.
 */

/** The service-relative path the API will see for `urlWithParams`, or null when it is not our API. */
export type ServiceUrlClassifier = (urlWithParams: string) => string | null;

/** Where the API roots come from. Functions, so a runtime config file can supply them after boot. */
export interface ApiRootClassifierOptions {
  /** The API roots, absolute (`https://api.example.test/v1`) or origin-relative (`/api`). */
  readonly roots: () => readonly string[];
  /** The page origin relative roots and URLs resolve against, e.g. `() => location.origin`. */
  readonly pageOrigin: () => string;
}

const PATH_SEPARATOR = '/';

/** A classifier that matches the URL against configured API roots by origin and path segment. */
export function createApiRootClassifier(options: ApiRootClassifierOptions): ServiceUrlClassifier {
  return (urlWithParams) => {
    const target = parseUrl(urlWithParams, options.pageOrigin());
    if (target === null || hasUserInfo(target)) return null;
    for (const root of options.roots()) {
      const servicePath = servicePathUnder(target, parseUrl(root, options.pageOrigin()));
      if (servicePath !== null) return servicePath;
    }
    return null;
  };
}

function parseUrl(value: string, base: string): URL | null {
  try {
    return new URL(value, base);
  } catch (error) {
    if (error instanceof TypeError) return null;
    throw error;
  }
}

/**
 * A `user@host` spelling of the API origin is refused: it is how a crafted URL makes a third-party
 * host look like ours in a string comparison, and nothing legitimate in the app produces one.
 */
function hasUserInfo(url: URL): boolean {
  return url.username !== '' || url.password !== '';
}

function servicePathUnder(target: URL, root: URL | null): string | null {
  if (root === null || hasUserInfo(root) || target.origin !== root.origin) return null;
  const rootPath = trimTrailingSeparators(root.pathname);
  const path = target.pathname;
  if (rootPath === '') return path;
  if (path === rootPath) return PATH_SEPARATOR;
  return path.startsWith(rootPath + PATH_SEPARATOR) ? path.slice(rootPath.length) : null;
}

function trimTrailingSeparators(path: string): string {
  let end = path.length;
  while (end > 0 && path[end - 1] === PATH_SEPARATOR) end -= 1;
  return path.slice(0, end);
}
