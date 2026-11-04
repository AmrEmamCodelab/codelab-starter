/**
 * The wire protocol names, derived from ONE product prefix.
 *
 * The C# half (Codelab.Security) derives the same names from the same prefix, so a fork that runs
 * `init.ps1` with its own prefix changes both ends at once. Every name below is part of the signed
 * bytes or of a header the server reads: a client and a server configured with different prefixes
 * cannot agree on a single signature, and the failure is a uniform 401.
 *
 * Names (for prefix `P`):
 * - canonical framing version `P-CANON-v1`
 * - request/response signature scheme `P-SIG-v1`, profiles `P-SIG-v1/REQ`, `P-SIG-v1/RESP`, `P-SIG-v1/KDF`
 * - opaque identifier spec `P-PUBLICID-v1`
 * - headers `X-P-Signature` and `X-P-Response-Signature`
 */

/** The revision suffix shared by every scheme name. A construction change bumps it on both ends. */
export const SCHEME_REVISION = 'v1';

/** The fixed prefix the golden vectors and the package tests are pinned to. Never a production prefix. */
export const TEST_PROTOCOL_PREFIX = 'CODELABTEST';

/** Upper-case ASCII letter, then 1 to 31 upper-case ASCII letters or digits. No separators. */
const PREFIX_SHAPE = /^[A-Z][A-Z0-9]{1,31}$/;

const NAME_SEPARATOR = '-';
const PROFILE_SEPARATOR = '/';
const HEADER_LEAD = 'X';

/** Every protocol name, derived from {@link SecurityProtocol.prefix}. Frozen. */
export interface SecurityProtocol {
  /** The product prefix, e.g. `CODELABTEST`. */
  readonly prefix: string;
  /** The canonical framing version tag, the first framed element of every canonical form. */
  readonly canonVersion: string;
  /** The signature scheme tag, the first field of both signature canonical forms. */
  readonly signatureVersion: string;
  /** The canonical profile of a request proof. */
  readonly requestProfile: string;
  /** The canonical profile of a response signature. */
  readonly responseProfile: string;
  /** The canonical profile of the server-side session key derivation (kept for parity and vectors). */
  readonly kdfProfile: string;
  /** The opaque identifier spec tag. */
  readonly publicIdSpec: string;
  /** The request header carrying the proof. */
  readonly signatureHeader: string;
  /** The response header carrying the server's signature. */
  readonly responseSignatureHeader: string;
}

/** The prefix does not have the shape both ends accept. A configuration fault, raised at startup. */
export class InvalidProtocolPrefixError extends Error {
  constructor(prefix: unknown) {
    super(
      `The security protocol prefix '${String(prefix)}' is not valid: use 2 to 32 upper-case ASCII ` +
        'letters and digits, starting with a letter.',
    );
    this.name = 'InvalidProtocolPrefixError';
  }
}

/** True when `prefix` can name a protocol. */
export function isValidProtocolPrefix(prefix: unknown): prefix is string {
  return typeof prefix === 'string' && PREFIX_SHAPE.test(prefix);
}

/**
 * Derives every protocol name from `prefix`.
 *
 * @throws InvalidProtocolPrefixError when `prefix` is not 2 to 32 upper-case ASCII letters and digits.
 */
export function createSecurityProtocol(prefix: string): SecurityProtocol {
  if (!isValidProtocolPrefix(prefix)) throw new InvalidProtocolPrefixError(prefix);

  const signatureVersion = schemeName(prefix, 'SIG');
  return Object.freeze({
    prefix,
    canonVersion: schemeName(prefix, 'CANON'),
    signatureVersion,
    requestProfile: signatureVersion + PROFILE_SEPARATOR + 'REQ',
    responseProfile: signatureVersion + PROFILE_SEPARATOR + 'RESP',
    kdfProfile: signatureVersion + PROFILE_SEPARATOR + 'KDF',
    publicIdSpec: schemeName(prefix, 'PUBLICID'),
    signatureHeader: headerName(prefix, 'Signature'),
    responseSignatureHeader: headerName(prefix, 'Response-Signature'),
  });
}

function schemeName(prefix: string, scheme: string): string {
  return [prefix, scheme, SCHEME_REVISION].join(NAME_SEPARATOR);
}

function headerName(prefix: string, suffix: string): string {
  return [HEADER_LEAD, prefix, suffix].join(NAME_SEPARATOR);
}
