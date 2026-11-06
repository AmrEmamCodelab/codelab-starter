import type { ApiError } from '@codelab/ng-security';
import { NO_FIELD_ERRORS, fieldMessageKey, type FieldErrorMap } from '../../../patterns';
import { ValidationMessageCodes } from '../organizations/data/admin-api-error';
import {
  PortalContentLimits,
  SectionFields,
  type PortalContentDocument,
  type ReplacePortalContentBody,
} from './data/portal-content.models';

/** The four texts of a section. */
export interface SectionTexts {
  readonly titleAr: string;
  readonly titleEn: string;
  readonly bodyAr: string;
  readonly bodyEn: string;
}

/**
 * A section as the editor holds it before saving: its texts, the stored id (null for a new one) and a client key
 * that tracks the row while it is moved. The list order IS the sort order: the save numbers the sections 1..n.
 */
export interface DraftSection extends SectionTexts {
  readonly key: number;
  readonly id: string | null;
}

/** The first sort order the save writes; each next section gets the next number. */
export const FIRST_SORT_ORDER = 1;

/** The drafts of a stored page, in its sort order; `keyFor(index)` gives each row its client key. */
export function draftsOf(document: PortalContentDocument, keyFor: (index: number) => number): readonly DraftSection[] {
  return [...document.sections]
    .sort((left, right) => left.sortOrder - right.sortOrder)
    .map((section, index) => ({
      key: keyFor(index),
      id: section.id,
      titleAr: section.titleAr,
      titleEn: section.titleEn,
      bodyAr: section.bodyAr,
      bodyEn: section.bodyEn,
    }));
}

/** The replace body: every draft in list order, numbered from {@link FIRST_SORT_ORDER}. */
export function replaceBodyOf(drafts: readonly DraftSection[]): ReplacePortalContentBody {
  return {
    sections: drafts.map((draft, index) => ({
      id: draft.id,
      titleAr: draft.titleAr,
      titleEn: draft.titleEn,
      bodyAr: draft.bodyAr,
      bodyEn: draft.bodyEn,
      sortOrder: FIRST_SORT_ORDER + index,
    })),
  };
}

/** True when both lists hold the same sections (ids and texts) in the same order; client keys are ignored. */
export function sameSections(left: readonly DraftSection[], right: readonly DraftSection[]): boolean {
  return left.length === right.length && left.every((draft, index) => sameSection(draft, right[index]));
}

function sameSection(left: DraftSection, right: DraftSection): boolean {
  return left.id === right.id
    && left.titleAr === right.titleAr
    && left.titleEn === right.titleEn
    && left.bodyAr === right.bodyAr
    && left.bodyEn === right.bodyEn;
}

/** The list with the section at `index` moved by `offset` places; a move past either end leaves it unchanged. */
export function moveSection(drafts: readonly DraftSection[], index: number, offset: number): readonly DraftSection[] {
  const target = index + offset;
  if (index < 0 || index >= drafts.length || target < 0 || target >= drafts.length) {
    return drafts;
  }
  const moved = [...drafts];
  const [section] = moved.splice(index, 1);
  moved.splice(target, 0, section);
  return moved;
}

/** The list without the section whose client key is `key`. */
export function removeSection(drafts: readonly DraftSection[], key: number): readonly DraftSection[] {
  return drafts.filter((draft) => draft.key !== key);
}

/** The list with `section` replacing the draft of the same key, or appended when no draft has that key. */
export function upsertSection(drafts: readonly DraftSection[], section: DraftSection): readonly DraftSection[] {
  return drafts.some((draft) => draft.key === section.key)
    ? drafts.map((draft) => (draft.key === section.key ? section : draft))
    : [...drafts, section];
}

/** The texts with surrounding white space removed (inner blank lines, the paragraph breaks, are kept). */
export function trimmedTexts(texts: SectionTexts): SectionTexts {
  return {
    titleAr: texts.titleAr.trim(),
    titleEn: texts.titleEn.trim(),
    bodyAr: texts.bodyAr.trim(),
    bodyEn: texts.bodyEn.trim(),
  };
}

/**
 * The client check of a section (the same rules as the backend validator): every text is required, titles are at
 * most {@link PortalContentLimits.TitleMaxLength} and bodies {@link PortalContentLimits.BodyMaxLength} characters.
 * Messages are `errors.<CODE>` keys; an empty map means the section may be applied.
 */
export function sectionErrors(texts: SectionTexts): FieldErrorMap {
  const trimmed = trimmedTexts(texts);
  const errors: Record<string, string[]> = {};
  const check = (field: string, value: string, maxLength: number): void => {
    const code = textProblem(value, maxLength);
    if (code !== null) {
      errors[field] = [fieldMessageKey(code)];
    }
  };
  check(SectionFields.TitleAr, trimmed.titleAr, PortalContentLimits.TitleMaxLength);
  check(SectionFields.TitleEn, trimmed.titleEn, PortalContentLimits.TitleMaxLength);
  check(SectionFields.BodyAr, trimmed.bodyAr, PortalContentLimits.BodyMaxLength);
  check(SectionFields.BodyEn, trimmed.bodyEn, PortalContentLimits.BodyMaxLength);
  return Object.keys(errors).length === 0 ? NO_FIELD_ERRORS : errors;
}

function textProblem(value: string, maxLength: number): string | null {
  if (value === '') {
    return ValidationMessageCodes.Required;
  }
  return value.length > maxLength ? ValidationMessageCodes.TooLong : null;
}

/** A problem+json field path of one section (`sections[2].titleAr`, any case): group 1 is the index. */
const SECTION_FIELD_PATH = /^sections\[(\d+)\]/i;

/** The list positions (0-based) of the sections a refused save named in its field errors. */
export function sectionErrorIndexes(error: ApiError | null): ReadonlySet<number> {
  const indexes = new Set<number>();
  for (const { field } of error?.fieldErrors ?? []) {
    const match = SECTION_FIELD_PATH.exec(field);
    if (match) {
      indexes.add(Number(match[1]));
    }
  }
  return indexes;
}
