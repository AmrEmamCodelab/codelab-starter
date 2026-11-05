/**
 * How a session was started. Mirrors the backend's Identity `SignInMode` (stored as an int there too); the
 * values are the contract, so they never change. `TokenStore` remembers it, so logout knows whether the
 * identity provider's session must be ended as well.
 */
export enum SignInMode {
  Basic = 1,
  ActiveDirectory = 2,
  Sso = 3,
}

const SIGN_IN_MODES: ReadonlySet<number> = new Set([SignInMode.Basic, SignInMode.ActiveDirectory, SignInMode.Sso]);

/** Reads a stored mode; anything unknown (or a session stored before modes existed) is a basic session. */
export function parseSignInMode(raw: string | null): SignInMode {
  const value = Number(raw);
  return SIGN_IN_MODES.has(value) ? (value as SignInMode) : SignInMode.Basic;
}
