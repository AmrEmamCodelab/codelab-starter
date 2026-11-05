/** How deep a section sits in the table of contents. Integers, like every enum the starter owns. */
export enum TocDepth {
  Section = 1,
  Subsection = 2,
}

/**
 * One section of a portal content page, as the screen declares it.
 *
 * - `id` is a short stable slug (`eligibility`, `required-documents`): lower-case letters, digits and hyphens,
 *   starting with a letter. The page element carries `[portalSection]="id"`, which gives it the anchor id.
 * - `titleKey` is the translation key of the section title (the same key its heading shows).
 * - `depth` defaults to {@link TocDepth.Section}.
 */
export interface PortalSection {
  readonly id: string;
  readonly titleKey: string;
  readonly depth?: TocDepth;
}

/** One line of the table of contents. */
export interface TocEntry {
  readonly id: string;
  readonly anchorId: string;
  readonly titleKey: string;
  readonly depth: TocDepth;
}

/** What a section id must look like (it becomes part of an HTML id and a URL fragment). */
export const SECTION_ID_PATTERN = /^[a-z][a-z0-9-]*$/;

/** The prefix of every section anchor id, so a section id never clashes with another id on the page. */
export const SECTION_ANCHOR_PREFIX = 'section-';

/** The table of contents is shown only for a page with at least this many sections. */
export const MIN_TOC_ENTRIES = 2;

/** The HTML id (and URL fragment) of a section. */
export function sectionAnchorId(id: string): string {
  return `${SECTION_ANCHOR_PREFIX}${id}`;
}

/**
 * The table of contents for the screen's sections, in order. A section with an invalid or repeated id is left out
 * (the first wins), and a subsection before any section is shown as a section so no line is indented under nothing.
 */
export function buildToc(sections: readonly PortalSection[]): TocEntry[] {
  const seen = new Set<string>();
  const entries: TocEntry[] = [];
  for (const section of sections) {
    if (!SECTION_ID_PATTERN.test(section.id) || seen.has(section.id)) {
      continue;
    }
    seen.add(section.id);
    const wanted = section.depth ?? TocDepth.Section;
    const depth = entries.length === 0 ? TocDepth.Section : wanted;
    entries.push({ id: section.id, anchorId: sectionAnchorId(section.id), titleKey: section.titleKey, depth });
  }
  return entries;
}

/** The link of an entry: the current page path (with its query) plus the section fragment. */
export function tocHref(pagePath: string, anchorId: string): string {
  const hashAt = pagePath.indexOf('#');
  const path = hashAt === -1 ? pagePath : pagePath.slice(0, hashAt);
  return `${path}#${anchorId}`;
}

/**
 * The section the reader is in: the last one whose top edge is at or above `offset` (pixels from the viewport top),
 * else the first. `tops` maps anchor ids to their current top edge; a section missing from it is skipped.
 */
export function activeSection(entries: readonly TocEntry[], tops: ReadonlyMap<string, number>, offset: number): string | null {
  let active = entries[0]?.anchorId ?? null;
  for (const entry of entries) {
    const top = tops.get(entry.anchorId);
    if (top !== undefined && top <= offset) {
      active = entry.anchorId;
    }
  }
  return active;
}
