import type { Route } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { PERMISSION_GUARD_MARKER } from '../../../core/auth/permission.guard';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { SERVICE_REQUESTS_PERMISSION_KEYS } from '../data';
import { SERVICE_REQUESTS_REQUESTER_PATH } from './requester-paths';
import {
  SERVICE_REQUESTS_REQUESTER_NAV,
  SERVICE_REQUESTS_REQUESTER_ROUTES,
  ServiceRequestsRequesterNavIds,
} from './service-requests-requester.routes';

const [area] = SERVICE_REQUESTS_REQUESTER_ROUTES;
const pages: readonly Route[] = area?.children ?? [];

function pageAt(path: string): Route | undefined {
  return pages.find((route) => route.path === path);
}

describe('SERVICE_REQUESTS_REQUESTER_ROUTES', () => {
  it('mounts the area at service-requests', () => {
    expect(SERVICE_REQUESTS_REQUESTER_ROUTES).toHaveLength(1);
    expect(area?.path).toBe(SERVICE_REQUESTS_REQUESTER_PATH);
  });

  it('gates every page with the permission guard and its own key', () => {
    expect(pages.map((route) => route.path)).toEqual(['', 'mine', 'new', 'drafts/:id', ':id']);
    for (const route of pages) {
      expect(route.loadComponent).toBeTypeOf('function');
      expect(route.canActivate).toHaveLength(1);
      expect(Object.getOwnPropertyDescriptor(route.canActivate?.[0], PERMISSION_GUARD_MARKER)?.value).toBe(true);
      expect(route.data?.[RouteDataKeys.Permission]).toBeTruthy();
    }
  });

  it('asks create for the wizard and view-own for the rest', () => {
    const keyOf = (path: string) => pageAt(path)?.data?.[RouteDataKeys.Permission];
    expect(keyOf('')).toBe(SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn);
    expect(keyOf('mine')).toBe(SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn);
    expect(keyOf(':id')).toBe(SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn);
    expect(keyOf('new')).toBe(SERVICE_REQUESTS_PERMISSION_KEYS.Create);
    expect(keyOf('drafts/:id')).toBe(SERVICE_REQUESTS_PERMISSION_KEYS.Create);
  });

  it('lists the static paths before the id, so mine and new are never read as an id', () => {
    const paths = pages.map((route) => route.path);
    expect(paths.indexOf(':id')).toBe(paths.length - 1);
  });

  it('loads the right page for each path', async () => {
    const names = await Promise.all(pages.map(async (route) => ((await (route.loadComponent as () => Promise<{ name: string }>)()).name)));
    expect(names).toEqual([
      'RequesterDashboardComponent',
      'MyRequestsComponent',
      'ServiceRequestWizardComponent',
      'ServiceRequestWizardComponent',
      'ServiceRequestDetailComponent',
    ]);
  });
});

describe('SERVICE_REQUESTS_REQUESTER_NAV', () => {
  it('offers the dashboard, my requests and a new request, each with the key of its page', () => {
    const [group] = SERVICE_REQUESTS_REQUESTER_NAV;
    expect(group?.id).toBe(ServiceRequestsRequesterNavIds.Area);
    const expected = [
      { id: ServiceRequestsRequesterNavIds.Dashboard, link: '/service-requests', permission: SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn },
      { id: ServiceRequestsRequesterNavIds.Mine, link: '/service-requests/mine', permission: SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn },
      { id: ServiceRequestsRequesterNavIds.New, link: '/service-requests/new', permission: SERVICE_REQUESTS_PERMISSION_KEYS.Create },
    ];
    expect(group?.children?.map(({ id, link, permission }) => ({ id, link, permission }))).toEqual(expected);
  });

  it('never offers an entry without a gate', () => {
    for (const entry of SERVICE_REQUESTS_REQUESTER_NAV[0]?.children ?? []) expect(entry.permission).toBeTruthy();
  });
});
