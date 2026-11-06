import { RoutePaths } from '../../../core/routing/route-paths';

/** The root segment every absolute in-app link starts from. */
const ROOT_SEGMENT = '/';

/**
 * The segment the admin routes mount `SETTINGS_ROUTES` on (`/admin/settings`). `AdminRouteSegments` in
 * `admin.paths.ts` uses this same value, so the two never drift.
 */
export const SETTINGS_MOUNT_SEGMENT = 'settings';

/** The paths of `SETTINGS_ROUTES`, relative to the mount. */
export const SettingsRouteSegments = {
  List: '',
} as const;

/** Absolute router commands for the settings screen: `[routerLink]="SettingsPaths.list()"`. */
export const SettingsPaths = {
  list: (): readonly string[] => [ROOT_SEGMENT, RoutePaths.Admin, SETTINGS_MOUNT_SEGMENT],
} as const;
