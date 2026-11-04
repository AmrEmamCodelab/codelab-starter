/**
 * The short code a user reads out to the helpdesk: the first 10 hex characters of a 32-hex
 * correlation id, upper-cased and grouped five and five (`4bf92f3577...` becomes `4BF92-F3577`).
 *
 * The SERVER owns this rule (Codelab.Observability) and sends the result as `referenceCode`; this
 * copy covers ids the server did not label (a client-minted trace id for a request that never
 * reached it). Anything that is not exactly 32 ASCII hex characters is returned UNCHANGED: a code
 * that matches nothing in the log is worse than a long one that does.
 */

const GROUP_LENGTH = 5;
const GROUP_COUNT = 2;
const GROUP_SEPARATOR = '-';

/** ASCII only: `\d` and case folding must not admit a fullwidth digit. */
const W3C_TRACE_ID = /^[0-9A-Fa-f]{32}$/;
const MINTED_TRACE_ID = /^[0-9a-f]{32}$/;
const ALL_ZERO = /^0+$/;

/** The reference code for `correlationId`, or the id itself when it is not 32 hex characters. */
export function referenceCodeFrom(correlationId: string): string {
  if (!W3C_TRACE_ID.test(correlationId)) return correlationId;
  const prefix = correlationId.slice(0, GROUP_LENGTH * GROUP_COUNT).toUpperCase();
  return prefix.slice(0, GROUP_LENGTH) + GROUP_SEPARATOR + prefix.slice(GROUP_LENGTH);
}

/** True for a W3C trace id a client could have minted: 32 lower-case hex, not all zero. */
export function isClientTraceId(id: string | null | undefined): id is string {
  return typeof id === 'string' && MINTED_TRACE_ID.test(id) && !ALL_ZERO.test(id);
}
