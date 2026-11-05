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
