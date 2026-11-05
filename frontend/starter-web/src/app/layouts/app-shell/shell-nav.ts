import type { DgaIconName } from '@codelab/ng-dga';
import { PermissionKeys, type PermissionKey } from '../../core/permissions/permission-keys.generated';
import { RoutePaths, type RoutePath } from '../../core/routing/route-paths';

/** The root segment every absolute in-app link starts from. */
const ROOT_SEGMENT = '/';

/** One entry of the shell's main navigation. */
export interface ShellNavItem {
  readonly path: RoutePath;
  /** Translation key of the visible label. */
  readonly labelKey: string;
  readonly icon: DgaIconName;
  /** The key the user must hold to see the entry; the route itself is gated by the same key. */
  readonly permission: PermissionKey;
}

/** The main navigation, in display order. Add an entry here when a gated page joins the shell. */
export const SHELL_NAV_ITEMS: readonly ShellNavItem[] = [
  {
    path: RoutePaths.Dashboard,
    labelKey: 'layout.nav.dashboard',
    icon: 'dashboard',
    permission: PermissionKeys.DashboardView,
  },
  {
    path: RoutePaths.Admin,
    labelKey: 'layout.nav.admin',
    icon: 'lock',
    permission: PermissionKeys.AccessAdminView,
  },
];

/** The entries the user may open; an entry whose key the user lacks is not offered at all. */
export function visibleNavItems(
  items: readonly ShellNavItem[],
  holds: (permission: PermissionKey) => boolean,
): readonly ShellNavItem[] {
  return items.filter(item => holds(item.permission));
}

/** The router link of a top-level page, absolute so it works from any depth. */
export function appLink(path: RoutePath): readonly string[] {
  return path === RoutePaths.Dashboard ? [ROOT_SEGMENT] : [ROOT_SEGMENT, path];
}
