import { InjectionToken } from '@angular/core';
import type { DgaIconName, DgaNavItem } from '@codelab/ng-dga';
import { RoutePaths } from '../../core/routing/route-paths';
import {
  holdsPermissions,
  type PermissionMatchMode,
  type PermissionRequirement,
} from '../../shared/permissions/permission-match';
import { rootLink } from '../portal-shell/portal-nav';

/** One entry of the requester shell's header navigation, before the permission filter and translation. */
export interface RequesterNavItem {
  /** Stable key (tracking, element ids); unique among its siblings. */
  readonly id: string;
  /** Translation key of the visible label. */
  readonly labelKey: string;
  /** Absolute router link of a leaf entry (`/requests`); an entry with children ignores it. */
  readonly link?: string;
  readonly icon?: DgaIconName;
  /**
   * The key (or keys, matched by `permissionMode`) the entry needs; the route it opens is gated by the same key. No
   * requirement means every signed-in user sees it: use that only for pages open to every visitor, such as the portal
   * pages.
   */
  readonly permission?: PermissionRequirement;
  readonly permissionMode?: PermissionMatchMode;
  /** A submenu (one level in the header; a child's children render as an indented group). */
  readonly children?: readonly RequesterNavItem[];
}

/** What the app tells the requester shell: its navigation, the brand link and where "view all" notifications lead. */
export interface RequesterShellOptions {
  readonly navItems: readonly RequesterNavItem[];
  /** Absolute router link of the brand. */
  readonly homeLink: string;
  /** Absolute router link of the notifications feed page; null hides "view all". */
  readonly notificationsLink: string | null;
}

/** The P4 default: no navigation (the requester pages arrive in P7), the brand to the app root, no feed page. */
export const DEFAULT_REQUESTER_SHELL_OPTIONS: RequesterShellOptions = {
  navItems: [],
  homeLink: rootLink(RoutePaths.Dashboard),
  notificationsLink: null,
};

/**
 * The requester shell's options. The app provides them where it mounts the shell (the route's `providers`), because
 * the entries point into features, which a layout never imports.
 */
export const REQUESTER_SHELL_OPTIONS = new InjectionToken<RequesterShellOptions>('REQUESTER_SHELL_OPTIONS', {
  providedIn: 'root',
  factory: () => DEFAULT_REQUESTER_SHELL_OPTIONS,
});

function allowed(item: RequesterNavItem, holds: (key: string) => boolean): boolean {
  return item.permission === undefined || holdsPermissions(holds, item.permission, item.permissionMode);
}

function visibleItem(
  item: RequesterNavItem,
  holds: (key: string) => boolean,
  translate: (key: string) => string,
): DgaNavItem | null {
  if (!allowed(item, holds)) {
    return null;
  }
  const base = { id: item.id, label: translate(item.labelKey), icon: item.icon };
  if (item.children === undefined) {
    return item.link === undefined ? null : { ...base, link: item.link };
  }
  const children = visibleRequesterNav(item.children, holds, translate);
  return children.length === 0 ? null : { ...base, children };
}

/**
 * The entries the user may open, as `<dga-header>` items with translated labels. An entry whose permission the user
 * does not hold is dropped with its whole submenu; an entry with a submenu stays only while one of its children does,
 * and an entry with neither a link nor children is dropped. A grant or revoke shows as soon as `holds` answers
 * differently (the shell reads the permission store as a signal).
 */
export function visibleRequesterNav(
  items: readonly RequesterNavItem[],
  holds: (key: string) => boolean,
  translate: (key: string) => string,
): readonly DgaNavItem[] {
  return items.flatMap(item => {
    const visible = visibleItem(item, holds, translate);
    return visible === null ? [] : [visible];
  });
}
