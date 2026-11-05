/**
 * Paging math for the admin-list pattern. Pages are 1-based (page 1 is the first page), which is what the API's
 * `page` query parameter and the pager both use. Every function is total: a zero, negative or fractional input is
 * clamped, never thrown on, so a stale `total` from the server can never break the pager.
 */

/** The page size the pattern uses when the screen sets none. */
export const DEFAULT_PAGE_SIZE = 20;

/** How many numbered page buttons the pager shows around the current page (odd, so the current one is centred). */
export const PAGER_WINDOW = 5;

/** The first page. */
export const FIRST_PAGE = 1;

/** A gap in the numbered pager (`1 … 7 8 9 … 20`). */
export const PAGE_GAP = 'gap' as const;

/** The window extends on both sides of the current page. */
const BOTH_SIDES = 2;

/** A distance of 2 between shown pages skips exactly one page, which is shown instead of a gap. */
const ONE_PAGE_SKIPPED = 2;

export type PagerItem = number | typeof PAGE_GAP;

/** The 1-based row range a page shows, for "21–40 of 95". Both are 0 when there are no rows. */
export interface PageRange {
  readonly from: number;
  readonly to: number;
}

function wholePositive(value: number, fallback: number): number {
  return Number.isFinite(value) && value >= FIRST_PAGE ? Math.floor(value) : fallback;
}

/** Number of pages for `total` rows; at least 1, so an empty list still has page 1. */
export function pageCount(total: number, pageSize: number): number {
  const size = wholePositive(pageSize, DEFAULT_PAGE_SIZE);
  const rows = Number.isFinite(total) && total > 0 ? Math.floor(total) : 0;
  return Math.max(FIRST_PAGE, Math.ceil(rows / size));
}

/** `page` clamped into 1..pageCount. */
export function clampPage(page: number, total: number, pageSize: number): number {
  return Math.min(wholePositive(page, FIRST_PAGE), pageCount(total, pageSize));
}

/** The rows the (clamped) page shows. */
export function pageRange(page: number, total: number, pageSize: number): PageRange {
  const rows = Number.isFinite(total) && total > 0 ? Math.floor(total) : 0;
  if (rows === 0) {
    return { from: 0, to: 0 };
  }
  const size = wholePositive(pageSize, DEFAULT_PAGE_SIZE);
  const current = clampPage(page, rows, size);
  const from = (current - FIRST_PAGE) * size + FIRST_PAGE;
  return { from, to: Math.min(rows, current * size) };
}

/**
 * The numbered pager items: always the first and last page, a window of {@link PAGER_WINDOW} pages around the
 * current one, and a {@link PAGE_GAP} where pages are skipped. A gap never stands for a single page: that page is
 * shown instead.
 */
export function pagerItems(page: number, count: number, window: number = PAGER_WINDOW): PagerItem[] {
  const last = wholePositive(count, FIRST_PAGE);
  const current = Math.min(wholePositive(page, FIRST_PAGE), last);
  const half = Math.floor(wholePositive(window, FIRST_PAGE) / BOTH_SIDES);
  const start = Math.max(FIRST_PAGE, Math.min(current - half, last - BOTH_SIDES * half));
  const end = Math.min(last, start + BOTH_SIDES * half);
  const items: PagerItem[] = [];
  appendEdge(items, FIRST_PAGE, start);
  for (let item = start; item <= end; item++) {
    items.push(item);
  }
  appendTail(items, end, last);
  return items;
}

function appendEdge(items: PagerItem[], first: number, start: number): void {
  if (start <= first) {
    return;
  }
  items.push(first);
  if (start - first === ONE_PAGE_SKIPPED) {
    items.push(first + 1);
  } else if (start - first > ONE_PAGE_SKIPPED) {
    items.push(PAGE_GAP);
  }
}

function appendTail(items: PagerItem[], end: number, last: number): void {
  if (end >= last) {
    return;
  }
  if (last - end === ONE_PAGE_SKIPPED) {
    items.push(last - 1);
  } else if (last - end > ONE_PAGE_SKIPPED) {
    items.push(PAGE_GAP);
  }
  items.push(last);
}
