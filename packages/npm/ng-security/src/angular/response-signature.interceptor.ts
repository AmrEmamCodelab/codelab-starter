/**
 * The `{Prefix}-SIG-v1` response-verification interceptor.
 *
 * It must be the LAST entry in `withInterceptors([...])`, so it runs innermost:
 * 1. it must see the bytes, not a parsed object, so it issues the request as `arraybuffer`, verifies
 *    the exact bytes and only then decodes them for delivery;
 * 2. it reads the nonce the signer just set, which `signatureInterceptor` (directly outside it) put on
 *    the request and into the header that went on the wire;
 * 3. nothing may modify the response between reception and verification.
 *
 * When the request was not signed (signing off, or not our API) it passes straight through without
 * touching the injector or the response type.
 */

import {
  HttpErrorResponse,
  HttpEventType,
  HttpResponse,
  type HttpEvent,
  type HttpInterceptorFn,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, concatMap, from, of, throwError } from 'rxjs';
import { RequestNotSignedError, SIGNED_REQUEST_NONCE } from '../signing/request-signature.ts';
import { parseSignatureHeader } from '../signing/signature-header.ts';
import { decodeForDelivery, type ResponseType } from '../verification/response-body.ts';
import { verifyResponseSignature, type ReceivedResponse } from '../verification/response-signature.ts';
import { SECURITY_PROTOCOL, SESSION_VERIFICATION_KEY_SOURCE, type SessionVerificationKeySource } from './tokens.ts';
import type { SecurityProtocol } from '../protocol/security-protocol.ts';

const CONTENT_TYPE_HEADER = 'Content-Type';
const RAW_RESPONSE_TYPE = 'arraybuffer';

interface VerificationRun {
  readonly keys: SessionVerificationKeySource;
  readonly protocol: SecurityProtocol;
  readonly nonce: string;
  readonly requested: ResponseType;
}

/** Verifies every response to a signed request and refuses to deliver a body it could not verify. */
export const responseSignatureInterceptor: HttpInterceptorFn = (req, next) => {
  const nonce = req.context.get(SIGNED_REQUEST_NONCE);
  if (nonce === null) return next(req);

  const protocol = inject(SECURITY_PROTOCOL);
  // The nonce on the wire must be the one the context names; anything else means the request was
  // altered after signing, and the answer could not be bound to what was sent.
  if (parseSignatureHeader(req.headers.get(protocol.signatureHeader))?.nonce !== nonce) {
    return throwError(() => new RequestNotSignedError('The signature header was altered after signing.'));
  }
  const run: VerificationRun = { keys: inject(SESSION_VERIFICATION_KEY_SOURCE), protocol, nonce, requested: req.responseType };
  return next(req.clone({ responseType: RAW_RESPONSE_TYPE })).pipe(
    // Before the success mapping, so an error THIS interceptor raises is not fed back into it.
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) return throwError(() => error);
      return from(verifyErrorResponse(run, error)).pipe(concatMap((decoded) => throwError(() => decoded)));
    }),
    concatMap((event: HttpEvent<unknown>) =>
      event.type === HttpEventType.Response ? from(verifySuccessResponse(run, event)) : of(event),
    ),
  );
};

/** Verifies the bytes when a key is held; a null key means responses are not being checked. */
async function verifyIfKeyed(run: VerificationRun, received: ReceivedResponse): Promise<void> {
  const key = await run.keys.responseVerificationKey();
  if (key === null) return;
  await verifyResponseSignature({ protocol: run.protocol, key, requestNonce: run.nonce }, received);
}

function received(run: VerificationRun, response: HttpResponse<unknown> | HttpErrorResponse, body: unknown): ReceivedResponse {
  return {
    status: response.status,
    contentType: response.headers.get(CONTENT_TYPE_HEADER) ?? '',
    // Requested as arraybuffer; anything else is impossible and fails the MAC rather than passing.
    body: body instanceof ArrayBuffer ? body : null,
    signatureHeader: response.headers.get(run.protocol.responseSignatureHeader),
    url: response.url,
  };
}

async function verifySuccessResponse(run: VerificationRun, response: HttpResponse<unknown>): Promise<HttpEvent<unknown>> {
  const arrived = received(run, response, response.body);
  await verifyIfKeyed(run, arrived);
  const decoded = decodeForDelivery(arrived.body, run.requested, arrived.contentType);
  const init = { headers: response.headers, status: response.status, statusText: response.statusText, url: response.url ?? undefined };
  if (decoded.jsonParseError !== null) {
    // What Angular does with an unparsable body: fail with the parse error and the original text.
    throw new HttpErrorResponse({ ...init, error: { error: decoded.jsonParseError, text: decoded.text } });
  }
  return new HttpResponse<unknown>({ ...init, body: decoded.value });
}

/**
 * Verifies an error response (when it carries a header) and decodes its body. Decoding is not
 * cosmetic: the signer's stale retry and an auth interceptor read `error.code` off a 401.
 */
async function verifyErrorResponse(run: VerificationRun, error: HttpErrorResponse): Promise<HttpErrorResponse> {
  // A transport failure carries a ProgressEvent, not a body: nothing to verify or decode.
  if (!(error.error instanceof ArrayBuffer)) return error;
  const arrived = received(run, error, error.error);
  await verifyIfKeyed(run, arrived);
  const decoded = decodeForDelivery(arrived.body, run.requested, arrived.contentType);
  return new HttpErrorResponse({
    error: decoded.value,
    headers: error.headers,
    status: error.status,
    statusText: error.statusText,
    url: error.url ?? undefined,
  });
}
