import {
  HttpErrorResponse,
  HttpEventType,
  HttpHeaders,
  HttpRequest,
  HttpResponse,
  type HttpEvent,
  type HttpHandlerFn,
} from '@angular/common/http';
import { Injector, runInInjectionContext } from '@angular/core';
import { lastValueFrom, of, throwError, type Observable } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TEST_PROTOCOL, bufferOf, randomKeyBytes, sessionKeysFrom } from '../testing/test-support.ts';
import type { SessionSigningKeys } from '../session/session-keys.store.ts';
import { RequestNotSignedError, signRequest } from '../signing/request-signature.ts';
import { buildResponseSigningInputBytes, computeMac, hashBody } from '../signing/signature-canonical.ts';
import { formatResponseSignatureHeader } from '../signing/signature-header.ts';
import { ResponseSignatureError, ResponseSignatureFailures } from '../verification/response-signature.ts';
import { responseSignatureInterceptor } from './response-signature.interceptor.ts';
import { SECURITY_PROTOCOL, SESSION_VERIFICATION_KEY_SOURCE, responseVerificationKeySource } from './tokens.ts';

const JSON_TYPE = 'application/json';
let keys: SessionSigningKeys;
let verifyKey: CryptoKey | null;
let injector: Injector;

beforeEach(async () => {
  keys = await sessionKeysFrom(randomKeyBytes());
  verifyKey = keys.verify;
  injector = Injector.create({
    providers: [
      { provide: SECURITY_PROTOCOL, useValue: TEST_PROTOCOL },
      { provide: SESSION_VERIFICATION_KEY_SOURCE, useValue: { responseVerificationKey: () => verifyKey } },
    ],
  });
});

async function signed(responseType: 'json' | 'text' | 'blob' | 'arraybuffer' = 'json') {
  const request = new HttpRequest('GET', '/api/items', { responseType });
  return signRequest(request, { protocol: TEST_PROTOCOL, servicePath: '/items', keys, nowUnixSeconds: 1_900_000_000 });
}

/** The server's signature header for a response to `nonce`. */
async function signatureFor(nonce: string, status: number, body: ArrayBuffer, signer = keys) {
  const canonical = buildResponseSigningInputBytes(TEST_PROTOCOL, { requestNonce: nonce, status, contentType: JSON_TYPE, bodyHash: await hashBody(body) });
  return formatResponseSignatureHeader(await computeMac(signer.sign, canonical));
}

function headers(signature: string | null) {
  const base = new HttpHeaders({ 'Content-Type': JSON_TYPE });
  return signature === null ? base : base.set(TEST_PROTOCOL.responseSignatureHeader, signature);
}

function answer(event: () => Observable<HttpEvent<unknown>>) {
  const seen: HttpRequest<unknown>[] = [];
  const next: HttpHandlerFn = (req) => {
    seen.push(req);
    return event();
  };
  return { seen, next };
}

function run(req: HttpRequest<unknown>, next: HttpHandlerFn) {
  return lastValueFrom(runInInjectionContext(injector, () => responseSignatureInterceptor(req, next)));
}

describe('responseSignatureInterceptor', () => {
  it('passes an unsigned request through without touching the response type or the injector', async () => {
    const request = new HttpRequest('GET', '/assets/x.json');
    const backend = answer(() => of(new HttpResponse({ status: 200, body: { a: 1 } })));
    injector = Injector.create({ providers: [] });

    const response = (await run(request, backend.next)) as HttpResponse<unknown>;

    expect(backend.seen[0]).toBe(request);
    expect(response.body).toEqual({ a: 1 });
  });

  it('takes the response over as bytes, verifies them, and delivers the JSON the caller asked for', async () => {
    const { request, nonce } = await signed();
    const body = bufferOf('{"id":"x"}');
    const sig = await signatureFor(nonce, 200, body);
    const backend = answer(() => of(new HttpResponse({ status: 200, body, headers: headers(sig) })));

    const response = (await run(request, backend.next)) as HttpResponse<unknown>;

    expect(backend.seen[0].responseType).toBe('arraybuffer');
    expect(response.body).toEqual({ id: 'x' });
  });

  it('preserves a text response type', async () => {
    const { request, nonce } = await signed('text');
    const body = bufferOf('plain');
    const sig = await signatureFor(nonce, 200, body);

    const response = (await run(request, answer(() => of(new HttpResponse({ status: 200, body, headers: headers(sig) }))).next)) as HttpResponse<unknown>;

    expect(response.body).toBe('plain');
  });

  it('passes progress events through untouched', async () => {
    const { request } = await signed();
    const progress: HttpEvent<unknown> = { type: HttpEventType.Sent };

    expect(await run(request, answer(() => of(progress)).next)).toBe(progress);
  });

  it('delivers an unsigned 204, which the server does not sign by construction', async () => {
    const { request } = await signed();
    const response = (await run(request, answer(() => of(new HttpResponse({ status: 204, body: null, headers: headers(null) }))).next)) as HttpResponse<unknown>;

    expect(response.status).toBe(204);
  });

  it('skips verification when no session key is held any more', async () => {
    verifyKey = null;
    const { request } = await signed();
    const response = (await run(request, answer(() => of(new HttpResponse({ status: 200, body: bufferOf('{}'), headers: headers(null) }))).next)) as HttpResponse<unknown>;

    expect(response.body).toEqual({});
  });

  it('fails the call with a JSON parse error after a verified but unparsable body', async () => {
    const { request, nonce } = await signed();
    const body = bufferOf('<html>');
    const sig = await signatureFor(nonce, 200, body);

    await expect(run(request, answer(() => of(new HttpResponse({ status: 200, body, headers: headers(sig) }))).next)).rejects.toBeInstanceOf(HttpErrorResponse);
  });

  it('refuses to send a request whose signature header was altered after signing', async () => {
    const { request } = await signed();
    const altered = request.clone({ setHeaders: { [TEST_PROTOCOL.signatureHeader]: 'garbage' } });
    const backend = answer(() => of(new HttpResponse({ status: 200 })));

    await expect(run(altered, backend.next)).rejects.toBeInstanceOf(RequestNotSignedError);
    expect(backend.seen).toHaveLength(0);
  });

  describe('an unverified response is never rendered as data', () => {
    it.each([
      ['a 2xx that carries no signature header', 'none', ResponseSignatureFailures.missing],
      ['a body that was tampered with in transit', 'tampered', ResponseSignatureFailures.mismatch],
      ['a response signed with a different key', 'other-key', ResponseSignatureFailures.mismatch],
      ['a malformed signature header', 'malformed', ResponseSignatureFailures.malformed],
    ])('refuses %s', async (_label, variant, reason) => {
      const { request, nonce } = await signed();
      const body = bufferOf('{"id":"x"}');
      const signer = variant === 'other-key' ? await sessionKeysFrom(randomKeyBytes()) : keys;
      const valid = await signatureFor(nonce, 200, body, signer);
      const sig = variant === 'none' ? null : variant === 'malformed' ? '1.short' : valid;
      const delivered = variant === 'tampered' ? bufferOf('{"id":"y"}') : body;

      const error = await run(request, answer(() => of(new HttpResponse({ status: 200, body: delivered, headers: headers(sig) }))).next).catch((caught: unknown) => caught);

      expect(error).toBeInstanceOf(ResponseSignatureError);
      expect(error).not.toBeInstanceOf(HttpErrorResponse);
      expect((error as ResponseSignatureError).reason).toBe(reason);
    });
  });

  describe('error responses', () => {
    it('decodes an unsigned problem+json body so the stale retry can read its code', async () => {
      const { request } = await signed();
      const error = new HttpErrorResponse({ status: 401, error: bufferOf('{"code":"signature_stale"}'), headers: headers(null) });

      const caught = await run(request, answer(() => throwError(() => error)).next).catch((value: unknown) => value);

      expect(caught).toBeInstanceOf(HttpErrorResponse);
      expect((caught as HttpErrorResponse).error).toEqual({ code: 'signature_stale' });
    });

    it('verifies a signed 403 and delivers its decoded body', async () => {
      const { request, nonce } = await signed();
      const body = bufferOf('{"code":"FORBIDDEN"}');
      const error = new HttpErrorResponse({ status: 403, error: body, headers: headers(await signatureFor(nonce, 403, body)) });

      const caught = await run(request, answer(() => throwError(() => error)).next).catch((value: unknown) => value);

      expect((caught as HttpErrorResponse).error).toEqual({ code: 'FORBIDDEN' });
    });

    it('refuses a signed error whose body was tampered with', async () => {
      const { request, nonce } = await signed();
      const sig = await signatureFor(nonce, 403, bufferOf('{"code":"FORBIDDEN"}'));
      const error = new HttpErrorResponse({ status: 403, error: bufferOf('{"code":"OTHER"}'), headers: headers(sig) });

      await expect(run(request, answer(() => throwError(() => error)).next)).rejects.toBeInstanceOf(ResponseSignatureError);
    });

    it('passes a transport failure and a non-HTTP error through untouched', async () => {
      const { request } = await signed();
      const transport = new HttpErrorResponse({ status: 0, error: { type: 'error' } });
      const local = new Error('local');

      await expect(run(request, answer(() => throwError(() => transport)).next)).rejects.toBe(transport);
      await expect(run(request, answer(() => throwError(() => local)).next)).rejects.toBe(local);
    });
  });
});

describe('responseVerificationKeySource', () => {
  it('yields the verify handle only when the deployment signs responses', async () => {
    const on = responseVerificationKeySource(async () => ({ verify: keys.verify, verifyResponses: true }));
    const off = responseVerificationKeySource(async () => ({ verify: keys.verify, verifyResponses: false }));
    const none = responseVerificationKeySource(async () => null);

    expect(await on.responseVerificationKey()).toBe(keys.verify);
    expect(await off.responseVerificationKey()).toBeNull();
    expect(await none.responseVerificationKey()).toBeNull();
  });

  it('reads the keys on every call, so a logout takes effect immediately', async () => {
    const load = vi.fn(async () => null);
    const source = responseVerificationKeySource(load);
    await source.responseVerificationKey();
    await source.responseVerificationKey();

    expect(load).toHaveBeenCalledTimes(2);
  });
});
