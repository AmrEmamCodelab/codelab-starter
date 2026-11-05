import { InjectionToken } from '@angular/core';

/** How many items of a <dga-accordion> may be open at once. */
export const DgaAccordionMode = {
  /** Opening an item closes the others. */
  Single: 'single',
  /** Items open and close independently. */
  Multiple: 'multiple',
} as const;
export type DgaAccordionMode = (typeof DgaAccordionMode)[keyof typeof DgaAccordionMode];

/** The heading level (`aria-level`) that wraps each item header, chosen to fit the page outline. */
export const DgaAccordionHeadingLevel = {
  H2: 2,
  H3: 3,
  H4: 4,
  H5: 5,
  H6: 6,
} as const;
export type DgaAccordionHeadingLevel = (typeof DgaAccordionHeadingLevel)[keyof typeof DgaAccordionHeadingLevel];

/** The heading level used when the accordion does not set one. */
export const DGA_ACCORDION_DEFAULT_HEADING_LEVEL: DgaAccordionHeadingLevel = DgaAccordionHeadingLevel.H3;

/** The keys that move focus between accordion headers (WAI-ARIA accordion pattern). */
export const DgaAccordionKey = {
  ArrowUp: 'ArrowUp',
  ArrowDown: 'ArrowDown',
  Home: 'Home',
  End: 'End',
} as const;

/** What an accordion needs from one of its items. */
export interface DgaAccordionItemRef {
  /** Closes the item (single mode closes the others when one opens). */
  collapse(): void;
  /** Moves keyboard focus to the item's header button. */
  focusHeader(): void;
  /** A disabled item's header cannot take focus. */
  isDisabled(): boolean;
}

/** What an accordion item needs from the accordion around it. Items also work on their own, without one. */
export interface DgaAccordionParent {
  readonly headingLevel: () => DgaAccordionHeadingLevel;
  /** Called after the user opened or closed an item. */
  itemToggled(item: DgaAccordionItemRef, expanded: boolean): void;
  /** Moves focus for a header key press; true when the key was handled. */
  moveFocus(item: DgaAccordionItemRef, key: string): boolean;
}

/** Provided by <dga-accordion> to its items. */
export const DGA_ACCORDION_PARENT = new InjectionToken<DgaAccordionParent>('DGA_ACCORDION_PARENT');

const FORWARD = 1;
const BACKWARD = -1;

function wrap(index: number, length: number): number {
  return ((index % length) + length) % length;
}

function enabledFrom(disabled: readonly boolean[], start: number, step: number): number | null {
  for (let offset = 0; offset < disabled.length; offset += 1) {
    const index = wrap(start + offset * step, disabled.length);
    if (!disabled[index]) return index;
  }
  return null;
}

/**
 * The header a key moves focus to from `current` (an index into `disabled`, one flag per header), skipping disabled
 * headers and wrapping around, or null for any other key or when no header can take focus. Up and Down follow the
 * block direction, so the result is the same in RTL and LTR.
 */
export function nextAccordionHeaderIndex(disabled: readonly boolean[], current: number, key: string): number | null {
  if (disabled.length === 0) return null;
  switch (key) {
    case DgaAccordionKey.ArrowDown:
      return enabledFrom(disabled, current + FORWARD, FORWARD);
    case DgaAccordionKey.ArrowUp:
      return enabledFrom(disabled, current + BACKWARD, BACKWARD);
    case DgaAccordionKey.Home:
      return enabledFrom(disabled, 0, FORWARD);
    case DgaAccordionKey.End:
      return enabledFrom(disabled, disabled.length - 1, BACKWARD);
    default:
      return null;
  }
}
