import { AppLanguages, type AppLanguage } from '../../../core/i18n/language';
import type { PortalSection } from '../../../patterns';
import type { PortalContentSectionDto } from './portal-content.client';

/** Where a page's content stands. Integers, like every enum the starter owns. */
export enum PortalContentStatus {
  /** The request is on its way (also the state before the first load). */
  Loading = 1,
  /** The server answered; the page may still have no sections. */
  Loaded = 2,
  /** The request failed (offline, 5xx, unknown page); the page offers a retry. */
  Failed = 3,
}

/** One section in the reader's language, ready to render. */
export interface LocalizedSection {
  /** The server's section id: stable, unique on the page (tracking and the table-of-contents anchor). */
  readonly id: string;
  /**
   * The section's anchor slug on the page (`part-1`, `part-2`... in display order). The server's ids are opaque public
   * ids, not guaranteed to fit the table of contents' slug rule, so the anchor never derives from them.
   */
  readonly anchor: string;
  readonly title: string;
  /** The body split into paragraphs (on blank lines), each trimmed, none empty. */
  readonly paragraphs: readonly string[];
}

/** The prefix of a content section's anchor slug; the 1-based position follows. */
export const CONTENT_ANCHOR_PREFIX = 'part-';
const FIRST_POSITION = 1;

/** A blank line, possibly holding spaces, between two paragraphs (any line-ending style). */
const PARAGRAPH_BREAK = /\r?\n[^\S\r\n]*\r?\n/;

/** The paragraphs of a plain-text body: split on blank lines, trimmed, empty ones dropped. */
export function splitParagraphs(body: string): string[] {
  return body
    .split(PARAGRAPH_BREAK)
    .map(paragraph => paragraph.trim())
    .filter(paragraph => paragraph !== '');
}

/** The sections in `language`, ordered by `sortOrder` (the server's order breaks ties). */
export function localizeSections(
  sections: readonly PortalContentSectionDto[],
  language: AppLanguage,
): LocalizedSection[] {
  const arabic = language === AppLanguages.Arabic;
  return sections
    .map((section, index) => ({ section, index }))
    .sort((left, right) => left.section.sortOrder - right.section.sortOrder || left.index - right.index)
    .map(({ section }, position) => ({
      id: section.id,
      anchor: `${CONTENT_ANCHOR_PREFIX}${position + FIRST_POSITION}`,
      title: (arabic ? section.titleAr : section.titleEn).trim(),
      paragraphs: splitParagraphs(arabic ? section.bodyAr : section.bodyEn),
    }));
}

/**
 * The sections as the portal content pattern's table of contents takes them. The pattern pipes `titleKey` through the
 * translation pipe, which renders an unknown key as written, so the already translated title shows as is.
 */
export function contentTocSections(sections: readonly LocalizedSection[]): PortalSection[] {
  return sections.map(section => ({ id: section.anchor, titleKey: section.title }));
}
