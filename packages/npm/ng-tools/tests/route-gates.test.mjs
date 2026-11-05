import { describe, expect, it } from 'vitest';
import {
  PERMISSION_GUARD_MARKER,
  RouteFindingKinds,
  assertRoutesGated,
  displayRoutePath,
  findUngatedRoutes,
  formatRouteFindings,
  isMarkedPermissionGuard,
  joinRoutePath,
  markPermissionGuard,
} from '../src/routes/route-gates.mjs';

const KEY = 'permission';
const page = () => Promise.resolve(class {});
const permissionGuard = () => markPermissionGuard(() => true);
const allowAll = () => true;
const dataOnly = (permission) => ({ data: { [KEY]: permission } });
const gated = (permission) => ({ ...dataOnly(permission), canActivate: [permissionGuard()] });
const options = (extra = {}) => ({ permissionDataKey: KEY, publicPaths: ['login', '**', ''], ...extra });

describe('joinRoutePath and displayRoutePath', () => {
  it.each([
    ['', '', ''],
    ['', 'admin', 'admin'],
    ['admin', '', 'admin'],
    ['admin', 'users/:id', 'admin/users/:id'],
    ['/admin/', '/users', 'admin/users'],
    ['admin', undefined, 'admin'],
  ])('joins %j and %j into %j', (parent, child, expected) => {
    expect(joinRoutePath(parent, child)).toBe(expected);
  });

  it('shows the root as / and other paths with a leading slash', () => {
    expect(displayRoutePath('')).toBe('/');
    expect(displayRoutePath('admin/users')).toBe('/admin/users');
  });
});

describe('findUngatedRoutes', () => {
  const shell = (children) => ({ path: '', loadComponent: page, children });

  it('passes a table whose pages are gated or public', () => {
    const routes = [
      { path: 'login', loadComponent: page },
      shell([{ path: '', ...gated('dashboard.view'), loadComponent: page }]),
      { path: '**', loadComponent: page },
    ];
    expect(findUngatedRoutes(routes, options())).toEqual([]);
  });

  it('reports a page with no key and no public path by its full path', () => {
    const routes = [shell([{ path: 'reports', component: class {} }])];
    expect(findUngatedRoutes(routes, options())).toEqual([{ path: '/reports', kind: RouteFindingKinds.missingPermission }]);
  });

  it('does not let a public empty shell exempt its pathless child', () => {
    const routes = [shell([{ path: '', loadComponent: page }])];
    expect(findUngatedRoutes(routes, options())).toEqual([{ path: '/', kind: RouteFindingKinds.missingPermission }]);
  });

  it('inherits a key from any ancestor, also a componentless one', () => {
    const routes = [{ path: 'admin', ...gated('admin.view'), children: [{ path: 'area', children: [{ path: 'deep', loadComponent: page }] }] }];
    expect(findUngatedRoutes(routes, options())).toEqual([]);
  });

  it('exempts redirects and treats an empty or blank key as missing', () => {
    const routes = [
      { path: 'old', redirectTo: 'new' },
      { path: 'blank', data: { [KEY]: ' ' }, loadComponent: page },
      { path: 'other', data: { unrelated: 'x' }, loadComponent: page },
    ];
    expect(findUngatedRoutes(routes, options()).map((finding) => finding.path)).toEqual(['/blank', '/other']);
  });

  it('checks lazily loaded children passed in by the full path of their mount, from a map or an object', () => {
    const adminRoutes = [{ path: '', loadComponent: page }, { path: 'users', ...gated('users.view'), loadComponent: page }];
    const accessRoutes = [{ path: 'roles', loadComponent: page }];
    const routes = [shell([{ path: 'admin', loadChildren: page }])];
    const lazy = { admin: [...adminRoutes, { path: 'access', loadChildren: page }], 'admin/access': accessRoutes };
    const expected = [
      { path: '/admin', kind: RouteFindingKinds.missingPermission },
      { path: '/admin/access/roles', kind: RouteFindingKinds.missingPermission },
    ];
    expect(findUngatedRoutes(routes, options({ lazyChildren: lazy }))).toEqual(expected);
    expect(findUngatedRoutes(routes, options({ lazyChildren: new Map(Object.entries(lazy)) }))).toEqual(expected);
  });

  it('lets a gated lazy mount gate its children', () => {
    const routes = [{ path: 'admin', ...gated('admin.view'), loadChildren: page }];
    expect(findUngatedRoutes(routes, options({ lazyChildren: { '/admin/': [{ path: 'x', loadComponent: page }] } }))).toEqual([]);
  });

  it('reports a lazy mount whose children were not passed in', () => {
    const routes = [{ path: 'admin', ...gated('admin.view'), loadChildren: page }];
    expect(findUngatedRoutes(routes, options())).toEqual([{ path: '/admin', kind: RouteFindingKinds.unresolvedLazyChildren }]);
  });

  it('reports keys outside knownPermissions', () => {
    const routes = [{ path: 'a', ...gated('a.view'), loadComponent: page }, { path: 'b', ...gated('b.retired'), loadComponent: page }];
    expect(findUngatedRoutes(routes, options({ knownPermissions: ['a.view'] })))
      .toEqual([{ path: '/b', kind: RouteFindingKinds.unknownPermission, permission: 'b.retired' }]);
  });

  it('normalizes public paths given with slashes', () => {
    expect(findUngatedRoutes([{ path: 'auth/sso/callback', loadComponent: page }], options({ publicPaths: ['/auth/sso/callback/'] }))).toEqual([]);
  });

  it('needs the data key', () => {
    expect(() => findUngatedRoutes([], {})).toThrow(/permissionDataKey/);
  });
});

describe('the permission guard requirement', () => {
  const missingGuard = (path) => ({ path, kind: RouteFindingKinds.missingGuard });

  it('reports a page with a permission key but no guard at all', () => {
    const routes = [{ path: 'reports', ...dataOnly('dashboard.view'), loadComponent: page }];
    expect(findUngatedRoutes(routes, options({ knownPermissions: ['dashboard.view'] }))).toEqual([missingGuard('/reports')]);
  });

  it('reports a page whose only guard is not a permission guard (an allow-all or the auth guard)', () => {
    const routes = [{ path: '', canActivate: ['authGuard', allowAll], loadComponent: page, children: [
      { path: 'reports', ...dataOnly('users.view'), canActivate: [allowAll], loadComponent: page },
    ] }];
    expect(findUngatedRoutes(routes, options())).toEqual([missingGuard('/reports')]);
  });

  it('accepts the guard on the route itself through canActivate or canMatch', () => {
    for (const property of ['canActivate', 'canMatch']) {
      const routes = [{ path: 'a', ...dataOnly('a.view'), [property]: [permissionGuard()], loadComponent: page }];
      expect(findUngatedRoutes(routes, options())).toEqual([]);
    }
  });

  it("does not count a page's own canActivateChild, which Angular never runs for the page itself", () => {
    const routes = [{ path: 'reports', ...dataOnly('users.manage'), canActivateChild: [permissionGuard()], loadComponent: page }];
    expect(findUngatedRoutes(routes, options())).toEqual([missingGuard('/reports')]);
  });

  it("does not let an ancestor's canActivate enforce a key a descendant declares (the guard checks the ancestor's key)", () => {
    const routes = [{ path: 'admin', ...gated('access.admin.view'), children: [
      { path: 'secret', ...dataOnly('users.manage'), loadComponent: page },
    ] }];
    expect(findUngatedRoutes(routes, options())).toEqual([missingGuard('/admin/secret')]);
    const lazyMount = [{ path: 'admin', ...gated('access.admin.view'), loadChildren: page }];
    const lazy = { admin: [{ path: 'secret', ...dataOnly('users.manage'), loadComponent: page }] };
    expect(findUngatedRoutes(lazyMount, options({ lazyChildren: lazy }))).toEqual([missingGuard('/admin/secret')]);
  });

  it('lets a key-less descendant rely on the guarded ancestor that declares its effective key', () => {
    const routes = [{ path: 'admin', ...gated('access.admin.view'), children: [
      { path: 'area', children: [{ path: 'deep', loadComponent: page }] },
    ] }];
    expect(findUngatedRoutes(routes, options())).toEqual([]);
  });

  it('counts canActivateChild only above the route that declares the key, not on or below it', () => {
    const above = [{ path: 'admin', canActivateChild: [permissionGuard()], children: [
      { path: 'area', ...dataOnly('area.view'), children: [{ path: 'deep', loadComponent: page }] },
    ] }];
    expect(findUngatedRoutes(above, options())).toEqual([]);
    const onDeclaringRoute = [{ path: 'area', ...dataOnly('area.view'), canActivateChild: [permissionGuard()], children: [
      { path: 'deep', loadComponent: page },
    ] }];
    expect(findUngatedRoutes(onDeclaringRoute, options())).toEqual([missingGuard('/area/deep')]);
    const below = [{ path: 'area', ...dataOnly('area.view'), children: [
      { path: 'mid', canActivateChild: [permissionGuard()], children: [{ path: 'deep', loadComponent: page }] },
    ] }];
    expect(findUngatedRoutes(below, options())).toEqual([missingGuard('/area/mid/deep')]);
  });

  it('accepts a guard on an ancestor, also a lazy mount, and reports a data-only lazy child of an unguarded mount', () => {
    const guardedMount = [{ path: 'admin', canActivateChild: [permissionGuard()], loadChildren: page }];
    const lazy = { admin: [{ path: 'users', ...dataOnly('users.view'), loadComponent: page }] };
    expect(findUngatedRoutes(guardedMount, options({ lazyChildren: lazy }))).toEqual([]);
    const unguardedMount = [{ path: 'admin', loadChildren: page }];
    expect(findUngatedRoutes(unguardedMount, options({ lazyChildren: lazy }))).toEqual([missingGuard('/admin/users')]);
  });

  it('does not require a guard on a public page', () => {
    expect(findUngatedRoutes([{ path: 'login', ...dataOnly('a.view'), loadComponent: page }], options())).toEqual([]);
  });

  it('uses options.isPermissionGuard when given, and refuses one that is not a function', () => {
    const appGuard = () => true;
    const routes = [{ path: 'a', ...dataOnly('a.view'), canActivate: [appGuard], loadComponent: page }];
    expect(findUngatedRoutes(routes, options({ isPermissionGuard: (guard) => guard === appGuard }))).toEqual([]);
    expect(findUngatedRoutes(routes, options())).toEqual([missingGuard('/a')]);
    expect(() => findUngatedRoutes(routes, options({ isPermissionGuard: 'yes' }))).toThrow(/isPermissionGuard/);
  });

  it('marks a guard with a process-wide symbol and returns the same function', () => {
    const guard = () => true;
    expect(markPermissionGuard(guard)).toBe(guard);
    expect(guard[Symbol.for('codelab.ng-tools.permission-guard')]).toBe(true);
    expect(PERMISSION_GUARD_MARKER).toBe(Symbol.for('codelab.ng-tools.permission-guard'));
    expect(isMarkedPermissionGuard(guard)).toBe(true);
    expect(isMarkedPermissionGuard(allowAll)).toBe(false);
    expect(isMarkedPermissionGuard('permissionGuard')).toBe(false);
    expect(() => markPermissionGuard('x')).toThrow(TypeError);
  });

  it('explains the missing guard in the assertion message', () => {
    expect(() => assertRoutesGated([{ path: 'a', ...dataOnly('a.view'), loadComponent: page }], options()))
      .toThrow(/\/a has a permission key but no permission guard[\s\S]*canActivate: \[permissionGuard\(\)\]/);
  });
});

describe('assertRoutesGated and formatRouteFindings', () => {
  it('returns quietly when every page is gated', () => {
    expect(() => assertRoutesGated([{ path: 'login', loadComponent: page }], options())).not.toThrow();
  });

  it('throws one line per route with the rule id and the fix', () => {
    const routes = [{ path: 'a', loadComponent: page }, { path: 'b', loadChildren: page }];
    expect(() => assertRoutesGated(routes, options())).toThrow(/base\.fe-permission-gated[\s\S]*\/a renders a page[\s\S]*\/b lazy-loads[\s\S]*Fix: /);
  });

  it('uses a custom fix text', () => {
    expect(formatRouteFindings([{ path: '/a', kind: RouteFindingKinds.missingPermission }], 'see the recipe')).toMatch(/Fix: see the recipe$/);
    expect(() => assertRoutesGated([{ path: 'a', loadComponent: page }], options({ fix: 'read docs/recipes/add-permission.md' })))
      .toThrow(/read docs\/recipes\/add-permission\.md/);
  });
});
