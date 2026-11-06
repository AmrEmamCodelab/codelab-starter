import { describe, expect, it } from 'vitest';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { LookupsPermissionKeys } from './lookups-permissions';
import { LOOKUPS_ROUTES } from './lookups.routes';
import { LookupsPaths, LookupsRouteSegments } from './lookups.paths';

describe('LOOKUPS_ROUTES', () => {
  it('gates both pages with lookups.view and a permission guard', () => {
    expect(LOOKUPS_ROUTES.map((route) => route.path)).toEqual([LookupsRouteSegments.Types, LookupsRouteSegments.Items]);
    for (const route of LOOKUPS_ROUTES) {
      expect(route.data?.[RouteDataKeys.Permission]).toBe(LookupsPermissionKeys.View);
      expect(route.canActivate).toHaveLength(1);
    }
  });

  it('loads the page components lazily', async () => {
    const [types, items] = await Promise.all(LOOKUPS_ROUTES.map((route) => route.loadComponent?.()));
    expect((types as { name: string }).name).toBe('LookupTypesPageComponent');
    expect((items as { name: string }).name).toBe('LookupItemsPageComponent');
  });

  it('builds absolute links, the type key as its own segment', () => {
    expect(LookupsPaths.types()).toEqual(['/', 'admin', 'lookups']);
    expect(LookupsPaths.items('contact-topics')).toEqual(['/', 'admin', 'lookups', 'contact-topics']);
    expect(LookupsRouteSegments.Items).toBe('lookups/:type');
  });
});
