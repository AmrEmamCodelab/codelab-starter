// Pure decisions of the slot web server (static-server.mjs): which file answers a request path and with which content
// type. A slot serves a frozen development build of the web app; unknown routes fall back to index.html (the Angular
// router resolves them), and nothing outside the served folder is ever read.
import path from 'node:path';

export const INDEX_FILE = 'index.html';
export const DEFAULT_CONTENT_TYPE = 'application/octet-stream';
export const HttpStatus = Object.freeze({ ok: 200, notFound: 404, methodNotAllowed: 405 });
export const HttpMethod = Object.freeze({ get: 'GET', head: 'HEAD' });
export const READ_METHODS = Object.freeze([HttpMethod.get, HttpMethod.head]);
export const NO_STORE_HEADERS = Object.freeze({ 'Cache-Control': 'no-store' });
/** The server's CLI flags (run-local passes them). */
export const ServerFlags = Object.freeze({ dir: '--dir', port: '--port' });
/** Both loopback addresses, so http://localhost:<port> answers whichever one the browser resolves first. */
export const LOOPBACK_HOSTS = Object.freeze(['127.0.0.1', '::1']);

export const CONTENT_TYPES = Object.freeze({
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.map': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.txt': 'text/plain; charset=utf-8',
});

/** The content type of a file by its extension. */
export function contentTypeFor(file) {
  return CONTENT_TYPES[path.extname(file).toLowerCase()] ?? DEFAULT_CONTENT_TYPE;
}

/**
 * The file that answers `requestUrl` under `rootDir`, or null (404).
 * An existing file wins; a path without an extension (an app route such as /service-requests) gets index.html; a
 * path that escapes the folder or a missing asset gets null.
 * @param {string} rootDir absolute folder that is served
 * @param {string} requestUrl the request target (path and query)
 * @param {(file: string) => boolean} isFile
 */
export function resolveStaticFile(rootDir, requestUrl, isFile) {
  let pathname;
  try {
    pathname = decodeURIComponent(new URL(requestUrl, 'http://localhost').pathname);
  } catch {
    return null;
  }
  const root = path.resolve(rootDir);
  const candidate = path.resolve(root, `.${pathname}`);
  const inside = candidate === root || candidate.startsWith(`${root}${path.sep}`);
  if (!inside) return null;
  if (candidate !== root && isFile(candidate)) return candidate;
  const index = path.join(root, INDEX_FILE);
  if (path.extname(pathname) === '' && isFile(index)) return index;
  return null;
}

/** Parses `--dir <folder> --port <n>`; returns null when either is missing or the port is not a positive integer. */
export function parseServerArguments(argv) {
  const value = (flag) => {
    const at = argv.indexOf(flag);
    return at >= 0 ? argv[at + 1] : undefined;
  };
  const dir = value(ServerFlags.dir);
  const port = Number(value(ServerFlags.port));
  if (!dir || !Number.isInteger(port) || port <= 0) return null;
  return { dir, port };
}
