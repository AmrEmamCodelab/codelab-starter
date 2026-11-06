import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { TestBed } from '@angular/core/testing';
import { provideRouter, type Route } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { App } from './app';
import { APP_NAV_ITEMS } from './app-nav';
import { NOT_FOUND_PATH, SHELL_PATH, routes } from './app.routes';
import { authGuard } from './core/auth/auth.guard';
import { CORE_TRANSLATION_KEYS } from './core/i18n/core-translation-keys';
import { PermissionKeys } from './core/permissions/permission-keys.generated';
import { RouteDataKeys, RoutePaths } from './core/routing/route-paths';
import { ADMIN_AREA_PERMISSIONS } from './features/admin/admin-entry';
import { LoginFieldMessageKeys } from './features/auth/login-form';
import { SignInMessageKeys } from './features/auth/sign-in.service';
import { NOTIFICATIONS_ROUTE_PATH } from './features/notifications/notifications.routes';
import { LanguageNameKeys } from './layouts/language-switcher/language-switcher.component';
import { fakeAuth, layoutTestProviders } from './layouts/layout.testing';
import { SIGNED_IN_ROUTE_PATHS } from './public-routes';

const APP_DIR = dirname(fileURLToPath(import.meta.url));
const I18N_DIR = join(APP_DIR, '..', 'assets', 'i18n');
const TEMPLATE_ROOTS = ['layouts', 'features'].map(folder => join(APP_DIR, folder));
/** `'some.key' | t` in a template. */
const TEMPLATE_KEY = /'([a-z][\w.-]*)'\s*\|\s*t\b/g;

function route(path: string, from: readonly Route[] = routes): Route {
  const found = from.find(candidate => candidate.path === path);
  if (!found) {
    throw new Error(`no route '${path}'`);
  }
  return found;
}

function flatten(tree: Record<string, unknown>, prefix = ''): Set<string> {
  const keys = new Set<string>();
  for (const [name, value] of Object.entries(tree)) {
    const key = prefix === '' ? name : `${prefix}.${name}`;
    if (typeof value === 'string') {
      keys.add(key);
    } else {
      flatten(value as Record<string, unknown>, key).forEach(child => keys.add(child));
    }
  }
  return keys;
}

function templates(folder: string): string[] {
  return readdirSync(folder, { recursive: true, encoding: 'utf8' })
    .filter(file => file.endsWith('.html'))
    .map(file => readFileSync(join(folder, file), 'utf8'));
}

function templateKeys(): string[] {
  return TEMPLATE_ROOTS.flatMap(templates).flatMap(source => [...source.matchAll(TEMPLATE_KEY)].map(m => m[1] as string));
}

describe('App', () => {
  it('renders the router outlet and the error dialog', () => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), ...layoutTestProviders(fakeAuth())],
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('router-outlet')).not.toBeNull();
    expect(host.querySelector('app-error-dialog')).not.toBeNull();
  });
});

describe('routes', () => {
  it('serves login and the 403 page without a session', () => {
    expect(route(RoutePaths.Login).canActivate).toBeUndefined();
    expect(route(RoutePaths.Unauthorized).canActivate).toBeUndefined();
  });

  it('puts the gated pages inside the shell, behind authGuard', () => {
    const shell = route(SHELL_PATH);
    expect(shell.canActivate).toEqual([authGuard]);
    expect(shell.children?.map(child => child.path)).toEqual([RoutePaths.Dashboard, RoutePaths.Admin, NOTIFICATIONS_ROUTE_PATH]);
  });

  it('gates every shell page with a permission guard and a typed key', () => {
    const children = route(SHELL_PATH).children ?? [];
    const gate = (path: string) => route(path, children);
    expect(gate(RoutePaths.Dashboard).data?.[RouteDataKeys.Permission]).toBe(PermissionKeys.DashboardView);
    // The admin mount opens to any admin page key (ADMIN_AREA_PERMISSIONS); each admin page carries its own key.
    expect(gate(RoutePaths.Admin).data?.[RouteDataKeys.Permission]).toBeUndefined();
    expect(ADMIN_AREA_PERMISSIONS).toContain(PermissionKeys.AccessAdminView);
    // A signed-in page (SIGNED_IN_ROUTE_PATHS) has no key: the shell's authGuard is its gate.
    for (const child of children.filter(page => !SIGNED_IN_ROUTE_PATHS.includes(page.path ?? ''))) {
      expect(child.canActivate).toHaveLength(1);
      expect(child.loadComponent ?? child.loadChildren).toBeTypeOf('function');
    }
  });

  it('ends with the 404 catch-all', () => {
    expect(routes.at(-1)?.path).toBe(NOT_FOUND_PATH);
  });

  it('lazy-loads every page', async () => {
    const pages = [...routes, ...(route(SHELL_PATH).children ?? [])].filter(page => page.loadComponent !== undefined);
    for (const page of pages) {
      await expect(page.loadComponent?.()).resolves.toBeTypeOf('function');
    }
  });

  it('lazy-loads the admin area as its own route table', async () => {
    const admin = route(RoutePaths.Admin, route(SHELL_PATH).children ?? []);
    expect(admin.loadComponent).toBeUndefined();
    await expect(admin.loadChildren?.()).resolves.toEqual(expect.any(Array));
  });
});

describe('translations', () => {
  const bundles = ['ar', 'en'].map(language => ({
    language,
    keys: flatten(JSON.parse(readFileSync(join(I18N_DIR, `${language}.json`), 'utf8')) as Record<string, unknown>),
  }));

  const usedKeys = [
    ...CORE_TRANSLATION_KEYS,
    ...templateKeys(),
    ...APP_NAV_ITEMS.map(item => item.labelKey),
    ...Object.values(LanguageNameKeys),
    ...Object.values(SignInMessageKeys),
    ...Object.values(LoginFieldMessageKeys),
  ];

  it('finds the keys the templates use', () => {
    expect(templateKeys()).toContain('auth.login.title');
  });

  for (const { language, keys } of bundles) {
    it(`has every key the app uses in ${language}.json`, () => {
      expect(usedKeys.filter(key => !keys.has(key))).toEqual([]);
    });
  }
});
