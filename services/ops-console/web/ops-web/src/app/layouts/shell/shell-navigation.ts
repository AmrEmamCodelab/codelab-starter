import type { DgaIconName, DgaNavItem } from '@codelab/ng-dga';
import { CommonKeys } from '../../core/i18n/common-translation-keys';
import { RoutePaths, type RoutePath } from '../../core/routing/route-paths';

/** One shell navigation entry before translation. */
export interface ShellNavEntry {
  readonly path: RoutePath;
  readonly labelKey: string;
  readonly icon: DgaIconName;
}

/** The console's screens, in menu order. */
export const SHELL_NAV_ENTRIES: readonly ShellNavEntry[] = Object.freeze([
  { path: RoutePaths.Overview, labelKey: CommonKeys.Nav.Overview, icon: 'dashboard' },
  { path: RoutePaths.Reference, labelKey: CommonKeys.Nav.Reference, icon: 'search' },
  { path: RoutePaths.Jobs, labelKey: CommonKeys.Nav.Jobs, icon: 'clock' },
  { path: RoutePaths.Certificates, labelKey: CommonKeys.Nav.Certificates, icon: 'shield-check' },
]);

/** The header's nav items: each entry with its translated label and an absolute router link. */
export function shellNavItems(translate: (key: string) => string): readonly DgaNavItem[] {
  return SHELL_NAV_ENTRIES.map((entry) => ({
    id: entry.path,
    label: translate(entry.labelKey),
    link: `/${entry.path}`,
    icon: entry.icon,
  }));
}
