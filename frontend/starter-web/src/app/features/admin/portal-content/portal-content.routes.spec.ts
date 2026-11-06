import { describe, expect, it } from 'vitest';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { unsavedChangesGuard } from '../access/common/unsaved-changes.guard';
import { PortalContentPermissionKeys } from './portal-content-permissions';
import { PORTAL_CONTENT_MOUNT_SEGMENT, PortalContentPaths } from './portal-content.paths';
import { PORTAL_CONTENT_ROUTES } from './portal-content.routes';

describe('PORTAL_CONTENT_ROUTES', () => {
  it('gates the editor with portal.content.view and guards unsaved edits on leave', async () => {
    const [route] = PORTAL_CONTENT_ROUTES;
    expect(PORTAL_CONTENT_ROUTES).toHaveLength(1);
    expect(route.path).toBe(PORTAL_CONTENT_MOUNT_SEGMENT);
    expect(route.data?.[RouteDataKeys.Permission]).toBe(PortalContentPermissionKeys.View);
    expect(route.canActivate).toHaveLength(1);
    expect(route.canDeactivate).toEqual([unsavedChangesGuard]);
    expect(((await route.loadComponent?.()) as { name: string }).name).toBe('PortalContentPageComponent');
  });

  it('links to the editor with an absolute path', () => {
    expect(PortalContentPaths.editor()).toEqual(['/', 'admin', 'portal-content']);
  });
});
