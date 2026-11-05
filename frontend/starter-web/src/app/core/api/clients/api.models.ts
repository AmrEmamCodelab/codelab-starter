import type { SigningSessionBlock } from '@codelab/ng-security';

/**
 * Hand-written wire shapes for the P2 endpoints. They mirror the API contract (contract section 5);
 * once `npm run api:gen` output exists under core/api/generated, these may be replaced by the
 * generated types without changing the clients' signatures.
 */

export interface LoginRequest {
  readonly userName: string;
  readonly password: string;
}

export interface RefreshRequest {
  readonly refreshToken: string;
}

export interface SessionUser {
  /** The user's PublicId (an opaque string; never a database id). */
  readonly id: string;
  readonly displayName: string;
}

/** The response of login and refresh. */
export interface SessionResponse {
  readonly accessToken: string;
  readonly refreshToken: string;
  /** ISO-8601 instant the access token stops being accepted. */
  readonly accessTokenExpiresAt: string;
  /** Minutes of inactivity after which the browser ends the session. */
  readonly idleTimeoutMinutes: number;
  readonly user: SessionUser;
  /** Present only when the server signs requests; handed to the ng-security session key store. */
  readonly signing?: SigningSessionBlock;
}

export interface PermissionsResponse {
  readonly permissions: readonly string[];
}

export interface FeatureFlagsResponse {
  readonly flags: Readonly<Record<string, boolean>>;
}

/** GET api/dashboard and GET api/access/admin return an empty placeholder object in P2. */
export type EmptyResponse = Readonly<Record<string, never>>;

/** A translation bundle: nested objects of strings. */
export interface TranslationTree {
  readonly [key: string]: string | TranslationTree;
}

/** GET api/auth/modes: the sign-in methods this deployment enables (the API's `Auth:Modes`). */
export interface AuthModesResponse {
  readonly basic: boolean;
  readonly activeDirectory: boolean;
  readonly sso: boolean;
}

/** POST api/auth/sso/exchange: the one-time ticket the API put in the callback fragment. */
export interface SsoExchangeRequest {
  readonly ticket: string;
  /** The binding secret whose hash started the sign-in in this tab (the API refuses the ticket without it). */
  readonly bindingProof: string;
}

/**
 * POST api/auth/sso/logout answers 200 with this body when the session came from the identity provider
 * (its end_session URL, carrying id_token_hint and post_logout_redirect_uri), or 204 with none.
 */
export interface SsoLogoutResponse {
  readonly endSessionUrl: string;
}

/** POST api/auth/activate: the activation ticket from the link and the user's first password. */
export interface ActivateRequest {
  readonly ticket: string;
  readonly password: string;
}
