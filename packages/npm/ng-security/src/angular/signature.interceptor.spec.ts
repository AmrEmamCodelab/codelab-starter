import { HttpErrorResponse, HttpRequest, HttpResponse, type HttpEvent, type HttpHandlerFn } from '@angular/common/http';
import { Injector, runInInjectionContext } from '@angular/core';
import { lastValueFrom, of, throwError, type Observable } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryMarker, MemoryRecordStore, TEST_PROTOCOL, randomKeyBytes, signingBlock } from '../testing/test-support.ts';
import { SessionKeyStore } from '../session/session-keys.store.ts';
import { SIGNED_REQUEST_NONCE } from '../signing/request-signature.ts';
import { createApiRootClassifier } from '../signing/service-url.ts';
import { parseSignatureHeader } from '../signing/signature-header.ts';
import { SignatureProblemCodes } from '../signing/signature-problem-codes.ts';
import { SessionSigningKeyMissingError, signatureInterceptor } from './signature.interceptor.ts';
import { SECURITY_PROTOCOL, SIGNED_SERVICE_URL_CLASSIFIER } from './tokens.ts';

const HEADER = TEST_PROTOCOL.signatureHeader;
let marker: MemoryMarker;
let records: MemoryRecordStore;
let store: SessionKeyStore;
let injector: Injector;
let sent: HttpRequest<unknown>[];

function setUp(roots: string[] = ['/api']) {
  marker = new MemoryMarker();
  records = new MemoryRecordStore();
  store = new SessionKeyStore({ marker, records, clock: () => Date.now() });
  injector = Injector.create({
    providers: [
      { provide: SessionKeyStore, useValue: store },
      { provide: SECURITY_PROTOCOL, useValue: TEST_PROTOCOL },
      {
        provide: SIGNED_SERVICE_URL_CLASSIFIER,
        useValue: createApiRootClassifier({ roots: () => roots, pageOrigin: () => 'https://portal.example.test' }),
      },
    ],
  });
  sent = [];
}

/** A backend that records what reached it and answers with `answers` in turn. */
function backend(...answers: ((req: HttpRequest<unknown>) => Observable<HttpEvent<unknown>>)[]): HttpHandlerFn {
  return (req) => {
    sent.push(req);
    const answer = answers[sent.length - 1];
    return answer === undefined ? of(new HttpResponse({ status: 200 })) : answer(req);
  };
}

function run(req: HttpRequest<unknown>, next: HttpHandlerFn = backend()) {
  return lastValueFrom(runInInjectionContext(injector, () => signatureInterceptor(req, next)));
}

function rejected(code: string | null, status = 401) {
  return () => throwError(() => new HttpErrorResponse({ status, error: code === null ? null : { code } }));
}

beforeEach(() => setUp());

describe('signatureInterceptor', () => {
  describe('when the session holds a key', () => {
    beforeEach(async () => {
      await store.adopt(signingBlock(randomKeyBytes(), { serverTimeUnix: Math.floor(Date.now() / 1000) }));
    });

    it('signs a call to our own API and publishes the nonce', async () => {
      await run(new HttpRequest('GET', '/api/items'));

      const proof = parseSignatureHeader(sent[0].headers.get(HEADER));
      expect(proof).not.toBeNull();
      expect(sent[0].context.get(SIGNED_REQUEST_NONCE)).toBe(proof!.nonce);
    });

    it('draws a new nonce for every request', async () => {
      await run(new HttpRequest('GET', '/api/items'));
      await run(new HttpRequest('GET', '/api/items'));

      expect(sent[0].headers.get(HEADER)).not.toBe(sent[1].headers.get(HEADER));
    });

    it('does not sign a path outside the API root or a static asset', async () => {
      await run(new HttpRequest('GET', '/apix/items'));
      await run(new HttpRequest('GET', '/assets/i18n/ar.json'));

      expect(sent.map((req) => req.headers.has(HEADER))).toEqual([false, false]);
    });

    it('re-signs once with a fresh nonce on signature_stale and delivers the retry', async () => {
      const response = await run(new HttpRequest('GET', '/api/items'), backend(rejected(SignatureProblemCodes.stale)));

      expect(sent).toHaveLength(2);
      expect(parseSignatureHeader(sent[0].headers.get(HEADER))!.nonce).not.toBe(parseSignatureHeader(sent[1].headers.get(HEADER))!.nonce);
      expect((response as HttpResponse<unknown>).status).toBe(200);
    });

    it('surfaces a second stale rejection instead of retrying again', async () => {
      const stale = rejected(SignatureProblemCodes.stale);
      await expect(run(new HttpRequest('GET', '/api/items'), backend(stale, stale))).rejects.toMatchObject({ status: 401 });
      expect(sent).toHaveLength(2);
    });

    it.each([SignatureProblemCodes.replayed, SignatureProblemCodes.invalid, null])(
      'does not retry any other 401 (%s)',
      async (code) => {
        await expect(run(new HttpRequest('GET', '/api/items'), backend(rejected(code)))).rejects.toBeInstanceOf(HttpErrorResponse);
        expect(sent).toHaveLength(1);
      },
    );

    it('does not retry a stale code on another status', async () => {
      await expect(run(new HttpRequest('GET', '/api/items'), backend(rejected(SignatureProblemCodes.stale, 400)))).rejects.toBeDefined();
      expect(sent).toHaveLength(1);
    });
  });

  describe('when the deployment is not signing', () => {
    it('passes the request through unchanged and touches nothing else', async () => {
      const classify = vi.fn(() => '/items');
      injector = Injector.create({
        providers: [{ provide: SessionKeyStore, useValue: store }, { provide: SIGNED_SERVICE_URL_CLASSIFIER, useValue: classify }],
      });
      const request = new HttpRequest('POST', '/api/items', { a: 1 });

      await run(request);

      expect(sent[0]).toBe(request);
      expect(classify).not.toHaveBeenCalled();
    });
  });

  describe('when the session expects signing but holds no key', () => {
    beforeEach(() => marker.set());

    it('sends nothing at all and reports a distinguishable, non-HTTP failure', async () => {
      const error = await run(new HttpRequest('GET', '/api/items')).catch((caught: unknown) => caught);

      expect(sent).toHaveLength(0);
      expect(error).toBeInstanceOf(SessionSigningKeyMissingError);
      expect(error).not.toBeInstanceOf(HttpErrorResponse);
      expect((error as SessionSigningKeyMissingError).reason).toBe('signing_key_missing');
      expect((error as SessionSigningKeyMissingError).url).toBe('/api/items');
    });
  });

  describe('when the API is on its own origin', () => {
    beforeEach(async () => {
      setUp(['https://api.example.test']);
      await store.adopt(signingBlock(randomKeyBytes(), { serverTimeUnix: Math.floor(Date.now() / 1000) }));
    });

    it('signs a cross-origin call to the configured API origin', async () => {
      await run(new HttpRequest('GET', 'https://api.example.test/items'));
      expect(sent[0].headers.has(HEADER)).toBe(true);
    });

    it('does not sign another origin, the page itself, or a userinfo spelling', async () => {
      for (const url of ['https://other.example.test/items', '/items', 'https://api.example.test@evil.example/items']) {
        await run(new HttpRequest('GET', url));
      }
      expect(sent.every((req) => !req.headers.has(HEADER))).toBe(true);
    });
  });
});
