import { InjectionToken } from '@angular/core';
import type { DgaIconName } from '@codelab/ng-dga';
import type { PermissionKey } from '../../core/permissions/permission-keys.generated';
import { RoutePaths, type RoutePath } from '../../core/routing/route-paths';

/** The root segment every absolute in-app link starts from. */
const ROOT_SEGMENT = '/';

/** A page a navigation entry may open, and the key that opens it (the page's route is gated by the same key). */
export interface ShellNavTarget {
  readonly permission: PermissionKey;
  readonly link: readonly string[];
}

/** One entry of the shell's main navigation. */
export interface ShellNavItem {
  /** The top-level area the entry opens; identifies the entry. */
  readonly path: RoutePath;
  /** Translation key of the visible label. */
  readonly labelKey: string;
  readonly icon: DgaIconName;
  /**
   * The pages the entry may open, in order of preference. The entry is offered to a holder of any of their keys and
   * leads to the first one held.
   */
  readonly targets: readonly ShellNavTarget[];
}

/** A navigation entry the user may open, with the link it leads them to. */
export interface VisibleShellNavItem extends ShellNavItem {
  readonly link: readonly string[];
}

/** The router link of a top-level page, absolute so it works from any depth. */
export function appLink(path: RoutePath): readonly string[] {
  return path === RoutePaths.Dashboard ? [ROOT_SEGMENT] : [ROOT_SEGMENT, path];
}

/**
 * The main navigation, in display order. The app provides it where it mounts the shell (`SHELL_ROUTE` providers in
 * `app.routes.ts`, items in `app-nav.ts`), because the entries point into features, which a layout never imports.
 */
export const SHELL_NAV_ITEMS = new InjectionToken<readonly ShellNavItem[]>('SHELL_NAV_ITEMS');

/**
 * The entries the user may open, each with the link of its first target they hold; an entry none of whose keys the
 * user holds is not offered at all.
 */
export function visibleNavItems(
  items: readonly ShellNavItem[],
  holds: (permission: PermissionKey) => boolean,
): readonly VisibleShellNavItem[] {
  return items.flatMap(item => {
    const target = item.targets.find(candidate => holds(candidate.permission));
    return target ? [{ ...item, link: target.link }] : [];
  });
}
