import type { Route } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { PERMISSION_GUARD_MARKER } from '../../../core/auth/permission.guard';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { SERVICE_REQUESTS_ADMIN_ROUTES } from '../admin/service-requests-admin.routes';
import { ServiceRequestsAdminPaths } from '../admin/service-requests-admin.paths';
import { RecordsOversightPermissionKeyValue } from '../admin/service-requests-admin.permissions';
import { SERVICE_REQUESTS_PERMISSION_KEYS } from '../data';
import { ServiceRequestsStaffPaths } from './service-requests-staff.paths';
import { SERVICE_REQUESTS_STAFF_ROUTES } from './service-requests-staff.routes';

function pages(routes: readonly Route[]): readonly { readonly path: string; readonly route: Route }[] {
  const [mount] = routes;
  return (mount.children ?? []).map((route) => ({ path: [mount.path, route.path].filter(Boolean).join('/'), route }));
}

function isGated(route: Route): boolean {
  return (route.canActivate ?? []).some((guard) => Object.prototype.hasOwnProperty.call(guard, PERMISSION_GUARD_MARKER));
}

describe('service request staff and admin routes', () => {
  it('mounts the queue and the detail under staff/service-requests, both gated by the queue permission', () => {
    const staff = pages(SERVICE_REQUESTS_STAFF_ROUTES);
    expect(staff.map((page) => page.path)).toEqual(['staff/service-requests', 'staff/service-requests/:id']);
    for (const { route } of staff) {
      expect(isGated(route)).toBe(true);
      expect(route.data?.[RouteDataKeys.Permission]).toBe(SERVICE_REQUESTS_PERMISSION_KEYS.Queue);
      expect(route.loadComponent).toBeTypeOf('function');
    }
  });

  it('mounts the read-only oversight under requests (in the admin area), gated by records.oversight.view', () => {
    const admin = pages(SERVICE_REQUESTS_ADMIN_ROUTES);
    expect(admin.map((page) => page.path)).toEqual(['requests', 'requests/:id']);
    for (const { route } of admin) {
      expect(isGated(route)).toBe(true);
      expect(route.data?.[RouteDataKeys.Permission]).toBe(RecordsOversightPermissionKeyValue);
    }
  });

  it('builds absolute links that match the routes', () => {
    expect(ServiceRequestsStaffPaths.queue()).toEqual(['/', 'staff', 'service-requests']);
    expect(ServiceRequestsStaffPaths.detail('x')).toEqual(['/', 'staff', 'service-requests', 'x']);
    expect(ServiceRequestsStaffPaths.notifications()).toEqual(['/', 'notifications']);
    expect(ServiceRequestsAdminPaths.list()).toEqual(['/', 'admin', 'requests']);
    expect(ServiceRequestsAdminPaths.detail('x')).toEqual(['/', 'admin', 'requests', 'x']);
  });
});
