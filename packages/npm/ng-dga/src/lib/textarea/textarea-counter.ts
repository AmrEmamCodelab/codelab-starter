/** The placeholders a textarea counter template may use. */
export const DgaTextareaCounterPlaceholder = {
  /** The number of characters typed. */
  Count: '{count}',
  /** The maximum length. */
  Max: '{max}',
  /** The characters still allowed (never below zero). */
  Remaining: '{remaining}',
} as const;

/** The counter template used when the app gives none: digits only, so it needs no translation. */
export const DGA_TEXTAREA_DEFAULT_COUNTER_TEMPLATE = `${DgaTextareaCounterPlaceholder.Count} / ${DgaTextareaCounterPlaceholder.Max}`;

/** The rows a textarea shows when the app sets none. */
export const DGA_TEXTAREA_DEFAULT_ROWS = 4;

/** The values a counter template is filled with. */
export interface DgaTextareaCount {
  readonly count: number;
  readonly max: number;
}

/**
 * Fills a counter template, for example `'{count} of {max} characters'` (already translated), with the current
 * length, the maximum and the characters remaining. Every occurrence of a placeholder is replaced.
 */
export function formatTextareaCounter(template: string, { count, max }: DgaTextareaCount): string {
  const remaining = Math.max(max - count, 0);
  return template
    .replaceAll(DgaTextareaCounterPlaceholder.Count, String(count))
    .replaceAll(DgaTextareaCounterPlaceholder.Max, String(max))
    .replaceAll(DgaTextareaCounterPlaceholder.Remaining, String(remaining));
}
