/**
 * The browser half of `{Prefix}-PUBLICID-v1`: opaque, server-minted identifiers as branded types.
 *
 * Every surrogate row identifier travels as an opaque token (`m1_<keyId>_<48 base64url chars>`)
 * instead of a number. This file is the ONLY place an untrusted string becomes an identifier; the
 * `wire-id-cast` lint in `@codelab/ng-tools` forbids `as WireId<...>` everywhere else.
 *
 * Why a brand and not `type ItemId = string`: an alias brands nothing, so every id type is mutually
 * assignable and passing an order id where an item id belongs compiles. The phantom brand forces
 * construction through {@link tryAsId} / {@link asId}, which check the shape.
 *
 * What is deliberately absent: any conversion to `number`. A `WireId<D>` IS a string at runtime, so
 * widening to `string` is free (URLs, `===`, `Set`/`Map` keys, `track`, `HttpParams`). Only the
 * direction toward `number` is closed: `Number(token)` is `NaN`, and a `Set` of `NaN` silently
 * collapses distinct records into one.
 */

/** Phantom brand key: exists only in the type system, so no runtime code can read or forge it. */
declare const WIRE_ID_BRAND: unique symbol;

/**
 * An opaque identifier of domain `D`. Domains are spelled exactly as the server's id domains, because
 * the label is bound into the token's authenticated data: a typo is a 400 the server cannot explain.
 */
export type WireId<D extends string> = string & { readonly [WIRE_ID_BRAND]: D };

/** The wire version every token opens with. */
export const WIRE_ID_VERSION = 'm1';

/** Characters of the base64url payload (12-byte nonce + 16-byte tag + 8-byte ciphertext). */
export const WIRE_ID_PAYLOAD_LENGTH = 48;

/**
 * Shape ONLY: version, a 1 to 8 character lower-case key id, 48 base64url characters. It proves
 * nothing about the key, the tag or the domain; the server's AES-GCM tag check is authoritative.
 */
const WIRE_ID_SHAPE = new RegExp(`^${WIRE_ID_VERSION}_[a-z0-9]{1,8}_[A-Za-z0-9_-]{${WIRE_ID_PAYLOAD_LENGTH}}$`);

/** A domain label: lower-case words joined by single hyphens. */
const DOMAIN_SHAPE = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;

/** True when `raw` has the shape of a wire identifier. */
export function isWireId(raw: unknown): raw is string {
  return typeof raw === 'string' && WIRE_ID_SHAPE.test(raw);
}

/**
 * The one narrowing door: null when `raw` is absent or not shaped like a token.
 *
 * `domain` is REQUIRED although nothing checks it here: it is stated at every construction site
 * (a reviewer can compare it with the endpoint) and it lands the return type on the right brand
 * without a cast. It does NOT prove the domain: every family's token has the same grammar by design
 * (a domain on the wire would hand an observer a free classifier), so a well-formed token of another
 * family passes here and the server refuses it with an ordinary 400.
 */
export function tryAsId<D extends string>(domain: D, raw: string | null | undefined): WireId<D> | null {
  void domain;
  return isWireId(raw) ? (raw as WireId<D>) : null;
}

/** The domain did not hold a wire identifier. The message never contains the offending value. */
export class NotAWireIdError extends Error {
  constructor(domain: string) {
    super(`Not a wire identifier (expected domain '${domain}').`);
    this.name = 'NotAWireIdError';
  }
}

/** {@link tryAsId} that throws, for places where a missing identifier is a programming error. */
export function asId<D extends string>(domain: D, raw: string | null | undefined): WireId<D> {
  const id = tryAsId(domain, raw);
  if (id === null) throw new NotAWireIdError(domain);
  return id;
}

/**
 * Narrows a deliberately widened UI array (a multi-select emitting `Array<number | string>` because
 * it also carries numeric lookup options) into a set of branded ids. The replacement for
 * `new Set(values.map(Number))`: it throws on the first non-token instead of admitting `NaN`.
 */
export function toIdSet<D extends string>(domain: D, values: readonly (number | string)[]): Set<WireId<D>> {
  return new Set(values.map((value) => asId(domain, String(value))));
}

/**
 * Declares an application's id domains once, checked: kebab-case labels, no duplicates. Lookup ids
 * (public, enumerable reference data) stay plain numbers and do not belong here.
 *
 * @example
 * const IdDomains = defineIdDomains(['record', 'user', 'organization'] as const);
 * type RecordId = WireId<'record'>;
 */
export function defineIdDomains<const T extends readonly string[]>(domains: T): T {
  const seen = new Set<string>();
  for (const domain of domains) {
    if (!DOMAIN_SHAPE.test(domain)) throw new RangeError(`Id domain '${domain}' is not kebab-case.`);
    if (seen.has(domain)) throw new RangeError(`Id domain '${domain}' is declared twice.`);
    seen.add(domain);
  }
  return Object.freeze([...domains]) as unknown as T;
}
