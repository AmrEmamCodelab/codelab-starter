import type { DgaIconName } from '@codelab/ng-dga';

/**
 * One KPI tile of the landing dashboard ("Open requests: 12").
 *
 * - `labelKey` is the translation key of the label (unique per dashboard: it also tracks the tile).
 * - `value` is the server's figure: a number is formatted for the active language, a string is shown as it is
 *   (already formatted), null means not loaded yet.
 * - `link`, when set, makes the tile a link to the list behind the figure: a router path built from the route
 *   constants (never a literal), already filtered by the screen's permission checks.
 */
export interface KpiTile {
  readonly labelKey: string;
  readonly value: number | string | null;
  readonly icon?: DgaIconName;
  readonly link?: string | null;
}

/**
 * One quick action ("Submit a new request"): a link the screen offers only to users allowed to follow it (the
 * screen filters the list with its permission store before binding it).
 */
export interface QuickAction {
  readonly labelKey: string;
  readonly link: string;
  readonly icon?: DgaIconName;
}

/** True when the tile has a figure to show. */
export function hasKpiValue(value: KpiTile['value']): value is number | string {
  return typeof value === 'number' ? Number.isFinite(value) : typeof value === 'string' && value.trim().length > 0;
}

/** A tile's figure as text: numbers grouped for `language` (`ar`, `en`), strings as given, nothing as empty text. */
export function formatKpiValue(value: KpiTile['value'], language: string): string {
  if (!hasKpiValue(value)) {
    return '';
  }
  return typeof value === 'number' ? new Intl.NumberFormat(language).format(value) : value;
}

/** The tiles without duplicate labels (the first wins), so the row can track tiles by label key. */
export function uniqueTiles(tiles: readonly KpiTile[]): KpiTile[] {
  const seen = new Set<string>();
  return tiles.filter((tile) => !seen.has(tile.labelKey) && seen.add(tile.labelKey));
}
