const ID_SEPARATOR = ' ';

/** An id-list attribute value (aria-describedby, aria-labelledby) from the ids present, or null when there are none. */
export function joinIds(ids: readonly (string | null | undefined)[]): string | null {
  const present = ids.filter((id): id is string => typeof id === 'string' && id.length > 0);
  return present.length === 0 ? null : present.join(ID_SEPARATOR);
}

/** True when an optional message (for example an error text) is present and not empty. */
export function hasText(text: string | null | undefined): text is string {
  return typeof text === 'string' && text.length > 0;
}
