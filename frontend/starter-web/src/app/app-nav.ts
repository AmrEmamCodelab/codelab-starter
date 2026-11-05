import { PermissionKeys } from './core/permissions/permission-keys.generated';
import { RoutePaths } from './core/routing/route-paths';
import { ADMIN_ENTRIES } from './features/admin/admin-entry';
import { appLink, type ShellNavItem } from './layouts/app-shell/shell-nav';

/**
 * The staff shell's main navigation, in display order (provided as `SHELL_NAV_ITEMS` on `SHELL_ROUTE`). Add an entry
 * here when a gated area joins the shell; each target's key is the key that gates the page it opens.
 */
export const APP_NAV_ITEMS: readonly ShellNavItem[] = [
  {
    path: RoutePaths.Dashboard,
    labelKey: 'layout.nav.dashboard',
    icon: 'dashboard',
    targets: [{ permission: PermissionKeys.DashboardView, link: appLink(RoutePaths.Dashboard) }],
  },
  {
    path: RoutePaths.Admin,
    labelKey: 'layout.nav.admin',
    icon: 'lock',
    targets: ADMIN_ENTRIES,
  },
];
