import { describe, expect, it } from 'vitest';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { SettingsPermissionKeys } from './settings-permissions';
import { SETTINGS_ROUTES } from './settings.routes';
import { SETTINGS_MOUNT_SEGMENT, SettingsPaths } from './settings.paths';

describe('SETTINGS_ROUTES', () => {
  it('gates the page with settings.view', () => {
    const [page] = SETTINGS_ROUTES;
    expect(page.canActivate).toHaveLength(1);
    expect(page.data?.[RouteDataKeys.Permission]).toBe(SettingsPermissionKeys.View);
    expect(SettingsPermissionKeys).toEqual({ View: 'settings.view', Manage: 'settings.manage' });
  });

  it('lazy-loads the settings page', async () => {
    const component = await SETTINGS_ROUTES[0].loadComponent?.();
    expect((component as { name: string }).name).toBe('SettingsPageComponent');
  });

  it('links to /admin/settings', () => {
    expect(SettingsPaths.list()).toEqual(['/', 'admin', SETTINGS_MOUNT_SEGMENT]);
  });
});
