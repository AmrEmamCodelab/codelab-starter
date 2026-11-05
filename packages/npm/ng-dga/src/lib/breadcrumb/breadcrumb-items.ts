import type { DgaIconName } from '../icon/dga-icon-names.generated';

/** A router target for a crumb: a URL string or router commands. */
export type DgaBreadcrumbLink = string | unknown[];

/** One crumb of a <dga-breadcrumb> trail. `label` is already translated by the caller. */
export interface DgaBreadcrumbItem {
  readonly label: string;
  /** Router target; a crumb with a link that is not the current page renders as a link. */
  readonly link?: DgaBreadcrumbLink;
  /** Decorative leading icon. */
  readonly icon?: DgaIconName;
  /** Marks the current page. When no crumb is marked, the last crumb is the current page. */
  readonly current?: boolean;
}

/** A crumb with its resolved state, as the template renders it. */
export interface DgaResolvedBreadcrumb extends DgaBreadcrumbItem {
  readonly isCurrent: boolean;
  readonly isLink: boolean;
}

/**
 * Resolves which crumb is the current page and which crumbs are links. A crumb flagged `current: false` is never the
 * current page, even when it is last.
 */
export function resolveBreadcrumbs(items: readonly DgaBreadcrumbItem[]): DgaResolvedBreadcrumb[] {
  const anyFlagged = items.some((item) => item.current === true);
  const lastIndex = items.length - 1;
  return items.map((item, index) => {
    const isCurrent = item.current ?? (!anyFlagged && index === lastIndex);
    return { ...item, isCurrent, isLink: !isCurrent && item.link !== undefined };
  });
}
