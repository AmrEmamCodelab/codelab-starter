// The routes file of e2e/visual.mjs and the screenshot plan derived from it (pure, apart from loadRoutes reading a
// file through the given reader).
import path from 'node:path';
import { PNG_EXTENSION, TargetPlaceholders } from './constants.mjs';

/** A route name becomes part of a file name: lower-case letters, digits and dashes. */
const ROUTE_NAME = /^[a-z0-9][a-z0-9-]*$/;
const PATH_START = '/';
const INLINE_JSON_START = /^\s*[[{]/;
const SHOT_ID_SEPARATOR = '.';

/** Turns parsed routes JSON into { routes, mask, languageStorageKey } or { error }. */
export function normalizeRoutes(raw) {
  const document = Array.isArray(raw) ? { routes: raw } : raw;
  if (document === null || typeof document !== 'object' || !Array.isArray(document.routes)) {
    return { error: 'The routes file must be an array of routes or an object with a "routes" array.' };
  }
  if (document.routes.length === 0) return { error: 'The routes file lists no route.' };
  const extraMask = document.mask ?? [];
  if (!isStringList(extraMask)) return { error: '"mask" must be a list of CSS selectors.' };
  if (document.languageStorageKey !== undefined && typeof document.languageStorageKey !== 'string') {
    return { error: '"languageStorageKey" must be a string.' };
  }
  const routes = [];
  const names = new Set();
  for (const [index, route] of document.routes.entries()) {
    const where = `routes[${index}]`;
    if (route === null || typeof route !== 'object') return { error: `${where} must be an object.` };
    if (typeof route.name !== 'string' || !ROUTE_NAME.test(route.name)) return { error: `${where}.name must be lower-case letters, digits and dashes.` };
    if (names.has(route.name)) return { error: `${where}.name "${route.name}" is used twice.` };
    if (typeof route.path !== 'string' || !route.path.startsWith(PATH_START)) return { error: `${where}.path must start with /.` };
    if (route.waitFor !== undefined && typeof route.waitFor !== 'string') return { error: `${where}.waitFor must be a CSS selector.` };
    if (route.mask !== undefined && !isStringList(route.mask)) return { error: `${where}.mask must be a list of CSS selectors.` };
    names.add(route.name);
    routes.push({ name: route.name, path: route.path, waitFor: route.waitFor ?? null, mask: [...(route.mask ?? [])] });
  }
  return { routes, mask: [...extraMask], languageStorageKey: document.languageStorageKey ?? null };
}

function isStringList(value) {
  return Array.isArray(value) && value.every((item) => typeof item === 'string' && item.length > 0);
}

/** Reads --routes: inline JSON when it starts with [ or {, else a file read through readText(path). */
export function loadRoutes(value, readText) {
  let text;
  try {
    text = INLINE_JSON_START.test(value) ? value : readText(path.resolve(value));
  } catch (error) {
    return { error: `Cannot read the routes file ${value}: ${error.message}` };
  }
  try {
    return normalizeRoutes(JSON.parse(text));
  } catch (error) {
    return { error: `The routes file is not valid JSON: ${error.message}` };
  }
}

/** Every screenshot to take, route by route, then language, then width: [{ id, route, language, width }]. */
export function planShots(routes, languages, widths) {
  return routes.flatMap((route) =>
    languages.flatMap((language) =>
      widths.map((width) => ({ id: [route.name, language, width].join(SHOT_ID_SEPARATOR), route, language, width })),
    ),
  );
}

/** The baseline image of a shot: <dir>/<route>.<lang>.<width>.png. */
export function baselinePath(baselineDir, shot) {
  return path.join(baselineDir, `${shot.id}${PNG_EXTENSION}`);
}

/** The target image of a shot: the --target path with {route}, {lang} and {width} replaced. */
export function targetPath(pattern, shot) {
  return pattern
    .replaceAll(TargetPlaceholders.Route, shot.route.name)
    .replaceAll(TargetPlaceholders.Language, shot.language)
    .replaceAll(TargetPlaceholders.Width, String(shot.width));
}

/** The URL of a route below the base URL (a base path such as http://host/app/ is kept). */
export function routeUrl(baseUrl, routePath) {
  const base = baseUrl.endsWith(PATH_START) ? baseUrl : `${baseUrl}${PATH_START}`;
  return new URL(routePath.slice(PATH_START.length), base).href;
}
