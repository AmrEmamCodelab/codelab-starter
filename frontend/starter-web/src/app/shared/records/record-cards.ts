import type { DgaValueDirection } from '@codelab/ng-dga';

/** A field value as an adapter maps it: text and numbers are shown as they are; null, undefined and blank are "empty". */
export type RecordCardValue = string | number | null | undefined;

/**
 * One labelled value on a card. `labelKey` is the record type's translation key; `value` is DATA (a name, a number, a
 * formatted date or an already translated lookup name), never a translation key.
 *
 * - `valueDir: 'ltr'` keeps a phone number or a code readable inside Arabic text.
 * - `wide` lets a long value (a description) span the whole card row.
 */
export interface RecordCardField {
  readonly labelKey: string;
  readonly value: RecordCardValue;
  readonly valueDir?: DgaValueDirection;
  readonly wide?: boolean;
}

/** One card of the record's details, built by the record type's adapter (`<type>CardsAdapter(details)`). */
export interface RecordCard {
  readonly titleKey: string;
  readonly fields: readonly RecordCardField[];
}

/** A field as the cards component renders it: its text, or null to show the "not provided" text. */
export interface RenderedCardField {
  readonly field: RecordCardField;
  readonly text: string | null;
}

/** A card as the cards component renders it. */
export interface RenderedCard {
  readonly card: RecordCard;
  readonly fields: readonly RenderedCardField[];
}

/** The display text of a value, or null when it is empty (null, undefined, blank text or a number that is not finite). */
export function cardValueText(value: RecordCardValue): string | null {
  if (value === null || value === undefined) {
    return null;
  }
  if (typeof value === 'number') {
    return Number.isFinite(value) ? String(value) : null;
  }
  const text = value.trim();
  return text.length > 0 ? text : null;
}

/** The cards to render, in adapter order; a card without fields is left out (it would be an empty frame). */
export function renderCards(cards: readonly RecordCard[]): readonly RenderedCard[] {
  return cards
    .filter((card) => card.fields.length > 0)
    .map((card) => ({ card, fields: card.fields.map((field) => ({ field, text: cardValueText(field.value) })) }));
}
