// @vitest-environment node
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { baselinePath, loadRoutes, normalizeRoutes, planShots, routeUrl, targetPath } from '../lib/routes.mjs';

const LOGIN = { name: 'login', path: '/login' };

describe('normalizeRoutes', () => {
  it('accepts a plain array and fills the optional members', () => {
    expect(normalizeRoutes([LOGIN])).toEqual({ routes: [{ name: 'login', path: '/login', waitFor: null, mask: [] }], mask: [], languageStorageKey: null });
  });

  it('accepts the object form with file-wide masks and a storage key', () => {
    const result = normalizeRoutes({ routes: [{ ...LOGIN, waitFor: 'form', mask: ['.clock'] }], mask: ['.date'], languageStorageKey: 'app.lang' });
    expect(result).toEqual({ routes: [{ name: 'login', path: '/login', waitFor: 'form', mask: ['.clock'] }], mask: ['.date'], languageStorageKey: 'app.lang' });
  });

  it.each([
    [{}, 'The routes file must be an array of routes or an object with a "routes" array.'],
    [[], 'The routes file lists no route.'],
    [[{ name: 'Login', path: '/login' }], 'routes[0].name must be lower-case letters, digits and dashes.'],
    [[LOGIN, LOGIN], 'routes[1].name "login" is used twice.'],
    [[{ name: 'login', path: 'login' }], 'routes[0].path must start with /.'],
    [[{ ...LOGIN, waitFor: 1 }], 'routes[0].waitFor must be a CSS selector.'],
    [[{ ...LOGIN, mask: [''] }], 'routes[0].mask must be a list of CSS selectors.'],
    [{ routes: [LOGIN], mask: 'x' }, '"mask" must be a list of CSS selectors.'],
    [{ routes: [LOGIN], languageStorageKey: 1 }, '"languageStorageKey" must be a string.'],
  ])('refuses %j', (raw, message) => {
    expect(normalizeRoutes(raw)).toEqual({ error: message });
  });
});

describe('loadRoutes', () => {
  it('parses inline JSON without reading a file', () => {
    expect(loadRoutes(' [{"name":"login","path":"/login"}]', () => { throw new Error('no read'); }).routes).toHaveLength(1);
  });

  it('reads a file through the reader, and reports read and parse failures', () => {
    expect(loadRoutes('routes.json', () => JSON.stringify([LOGIN])).routes[0].name).toBe('login');
    expect(loadRoutes('missing.json', () => { throw new Error('ENOENT'); }).error).toMatch(/Cannot read the routes file missing.json: ENOENT/);
    expect(loadRoutes('bad.json', () => '{').error).toMatch(/not valid JSON/);
  });
});

describe('screenshot plan', () => {
  const routes = normalizeRoutes([LOGIN, { name: 'faq', path: '/portal/faq' }]).routes;

  it('goes route by route, then language, then width', () => {
    expect(planShots(routes, ['ar', 'en'], [1440, 390]).map((shot) => shot.id)).toEqual([
      'login.ar.1440',
      'login.ar.390',
      'login.en.1440',
      'login.en.390',
      'faq.ar.1440',
      'faq.ar.390',
      'faq.en.1440',
      'faq.en.390',
    ]);
  });

  it('names baselines and fills target placeholders', () => {
    const [shot] = planShots(routes, ['en'], [390]);
    expect(baselinePath('base', shot)).toBe(path.join('base', 'login.en.390.png'));
    expect(targetPath('t/{route}.{lang}.{width}.png', shot)).toBe('t/login.en.390.png');
    expect(targetPath('t/one.png', shot)).toBe('t/one.png');
  });

  it('joins route paths under the base URL, keeping a base path', () => {
    expect(routeUrl('http://localhost:4280', '/login')).toBe('http://localhost:4280/login');
    expect(routeUrl('http://host/app/', '/portal/faq?x=1')).toBe('http://host/app/portal/faq?x=1');
    expect(routeUrl('http://host/app', '/')).toBe('http://host/app/');
  });
});
