import type { DgaIconName } from '../icon/dga-icon-names.generated';

/**
 * One entry of the <dga-header> navigation. `label` is already translated. An entry with `children` opens a submenu
 * (its own `link` is not rendered: the button toggles the submenu); a child's own children render as an indented
 * group inside the same submenu.
 */
export interface DgaNavItem {
  /** Stable key (tracking, ids); unique among its siblings. */
  readonly id: string;
  readonly label: string;
  /** Router link of a leaf entry. */
  readonly link?: string;
  readonly children?: readonly DgaNavItem[];
  readonly icon?: DgaIconName;
  /**
   * Whether the link is current only on exactly its own URL (true) or also on any URL below it (false). Left out, it
   * is exact when another entry links below it (a portal home `/portal` next to `/portal/faq`), so one URL never marks
   * two entries as the current page; otherwise it also matches the pages below it.
   */
  readonly exact?: boolean;
}

/** The keys the header menus handle (WAI-ARIA disclosure navigation, with menu-style arrow keys in a submenu). */
export const DgaHeaderKey = {
  ArrowDown: 'ArrowDown',
  ArrowUp: 'ArrowUp',
  Home: 'Home',
  End: 'End',
  Enter: 'Enter',
  Space: ' ',
  Escape: 'Escape',
  Tab: 'Tab',
} as const;

const FORWARD = 1;
const BACKWARD = -1;

/** True when the entry opens a submenu. */
export function hasDgaNavChildren(item: DgaNavItem): boolean {
  return (item.children?.length ?? 0) > 0;
}

/** True when a key on a submenu button opens the submenu and moves focus into it. */
export function opensDgaSubmenu(key: string): boolean {
  return key === DgaHeaderKey.ArrowDown || key === DgaHeaderKey.Enter || key === DgaHeaderKey.Space;
}

function wrap(index: number, length: number): number {
  return ((index % length) + length) % length;
}

/**
 * The submenu link a key moves focus to from `current` among `count` links, wrapping around, or null for a key that
 * does not move (the caller lets it through). `current` is -1 when focus is not on a link yet.
 */
export function nextDgaMenuIndex(count: number, current: number, key: string): number | null {
  if (count === 0) return null;
  switch (key) {
    case DgaHeaderKey.ArrowDown:
      return wrap(current + FORWARD, count);
    case DgaHeaderKey.ArrowUp:
      return current < 0 ? count - 1 : wrap(current + BACKWARD, count);
    case DgaHeaderKey.Home:
      return 0;
    case DgaHeaderKey.End:
      return count - 1;
    default:
      return null;
  }
}

const PATH_SEPARATOR = '/';

function navLinks(items: readonly DgaNavItem[]): string[] {
  return items.flatMap((item) => [...(item.link === undefined ? [] : [item.link]), ...navLinks(item.children ?? [])]);
}

function withoutTrailingSeparator(link: string): string {
  return link.endsWith(PATH_SEPARATOR) ? link.slice(0, -PATH_SEPARATOR.length) : link;
}

/** True when `candidate` is a URL below `link` (`/portal/faq` is below `/portal`; `/portal` is below `/`). */
function isBelow(candidate: string, link: string): boolean {
  return candidate !== link && candidate.startsWith(`${withoutTrailingSeparator(link)}${PATH_SEPARATOR}`);
}

/**
 * The links among `items` (at any depth) that are current only on exactly their own URL: those whose entry sets
 * `exact: true`, plus those that leave it out while another entry links below them.
 */
export function exactDgaNavLinks(items: readonly DgaNavItem[]): ReadonlySet<string> {
  const links = navLinks(items);
  const exact = new Set<string>();
  const visit = (entries: readonly DgaNavItem[]): void => {
    for (const entry of entries) {
      const link = entry.link;
      if (link !== undefined && (entry.exact ?? links.some((other) => isBelow(other, link)))) exact.add(link);
      visit(entry.children ?? []);
    }
  };
  visit(items);
  return exact;
}
