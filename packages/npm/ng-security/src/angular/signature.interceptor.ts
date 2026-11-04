/**
 * The `{Prefix}-SIG-v1` request-signing interceptor.
 *
 * Order in `withInterceptors([...])`: after every interceptor that changes the request, immediately
 * before `responseSignatureInterceptor` (which must be last). Nothing may modify a request after it
 * is signed: a later interceptor that rewrites the URL or re-serialises the body invalidates every
 * proof, and the symptom is a uniform 401 with nothing in either log naming the cause.
 *
 * When the server does not sign (no signing block at login) this is a synchronous pass-through after
 * one marker read: nothing parsed, hashed or derived, no promise in the request path.
 */

import { HttpErrorResponse, type HttpInterceptorFn, type HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import type { SecurityProtocol } from '../protocol/security-protocol.ts';
import { SessionKeyStore } from '../session/session-keys.store.ts';
import { SigningRefusalReasons } from '../session/signing-material.ts';
import { signRequest } from '../signing/request-signature.ts';
import { SignatureProblemCodes } from '../signing/signature-problem-codes.ts';
import { SECURITY_PROTOCOL, SIGNED_SERVICE_URL_CLASSIFIER } from './tokens.ts';

const UNAUTHORIZED = 401;

/**
 * The session was issued signing material and the key is gone, so nothing was sent. Not an
 * `HttpErrorResponse`, so an auth interceptor cannot mistake it for expiry: the session must be
 * cleared and the user sent to login with a message that does NOT say "expired".
 */
export class SessionSigningKeyMissingError extends Error {
  readonly reason = SigningRefusalReasons.keyMissing;

  /** The refused request URL, for logs. Carries no proof material and no key. */
  readonly url: string;

  constructor(url: string) {
    super(
      'This session holds no signing key, so the request was not sent. The session must be cleared ' +
        'and re-established; it is not an expired token.',
    );
    this.name = 'SessionSigningKeyMissingError';
    this.url = url;
  }
}

interface SigningRun {
  readonly request: HttpRequest<unknown>;
  readonly servicePath: string;
  readonly store: SessionKeyStore;
  readonly protocol: SecurityProtocol;
}

/** Signs every call to our own API, and nothing else. */
export const signatureInterceptor: HttpInterceptorFn = (req, next) => {
  const store = inject(SessionKeyStore);
  if (!store.expectsSigning()) return next(req);

  const servicePath = inject(SIGNED_SERVICE_URL_CLASSIFIER)(req.urlWithParams);
  if (servicePath === null) return next(req);

  const run: SigningRun = { request: req, servicePath, store, protocol: inject(SECURITY_PROTOCOL) };
  return from(prepare(run)).pipe(
    switchMap((signed) => next(signed)),
    catchError((error: unknown) => {
      if (!isStaleRejection(error)) return throwError(() => error);
      // Exactly one re-sign with a FRESH nonce and timestamp (reusing the nonce would make the retry
      // a replay). Safe: a stale proof is refused before the endpoint runs and before the replay
      // store consumes anything. The inner chain has no catchError, so a second stale surfaces.
      return from(prepare(run)).pipe(switchMap((resigned) => next(resigned)));
    }),
  );
};

/** The request to send, or a refusal. Never the unsigned original. */
async function prepare(run: SigningRun): Promise<HttpRequest<unknown>> {
  const keys = await run.store.load();
  if (keys === null) throw new SessionSigningKeyMissingError(run.request.url);
  const signed = await signRequest(run.request, {
    protocol: run.protocol,
    servicePath: run.servicePath,
    keys,
    nowUnixSeconds: run.store.nowUnixSeconds(),
  });
  return signed.request;
}

/** A 401 whose problem+json `code` says the proof was outside the freshness window. */
function isStaleRejection(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status !== UNAUTHORIZED) return false;
  const code = (error.error as { code?: unknown } | null | undefined)?.code;
  return code === SignatureProblemCodes.stale;
}
