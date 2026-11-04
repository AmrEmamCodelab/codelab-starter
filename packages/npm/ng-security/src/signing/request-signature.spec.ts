import { createHash, createHmac } from 'node:crypto';
import { HttpHeaders, HttpParams, HttpRequest } from '@angular/common/http';
import { beforeAll, describe, expect, it } from 'vitest';
import { TEST_PROTOCOL, randomKeyBytes, sessionKeysFrom } from '../testing/test-support.ts';
import type { SessionSigningKeys } from '../session/session-keys.store.ts';
import { RequestNotSignedError, SIGNED_REQUEST_NONCE, randomNonce, signRequest } from './request-signature.ts';
import { UNSIGNED_BODY, buildRequestSigningInputBytes } from './signature-canonical.ts';
import { NONCE_LENGTH, parseSignatureHeader } from './signature-header.ts';

const NOW = 1_900_000_000;
const HEADER = TEST_PROTOCOL.signatureHeader;
let keyBytes: Uint8Array<ArrayBuffer>;
let keys: SessionSigningKeys;

beforeAll(async () => {
  keyBytes = randomKeyBytes();
  keys = await sessionKeysFrom(keyBytes);
});

function sign(request: HttpRequest<unknown>, servicePath = '/items', nonce?: string) {
  return signRequest(request, { protocol: TEST_PROTOCOL, servicePath, keys, nowUnixSeconds: NOW, nonce });
}

/** Recomputes the MAC the server would, from the fields it would see, with node:crypto. */
function serverMac(fields: { method: string; path: string; query: string; body: string | Uint8Array; nonce: string }) {
  const bodyHash = createHash('sha256').update(fields.body).digest('base64url');
  const canonical = buildRequestSigningInputBytes(TEST_PROTOCOL, {
    audience: keys.audience,
    method: fields.method,
    path: fields.path,
    query: fields.query,
    bodyHash,
    timestamp: String(NOW),
    nonce: fields.nonce,
  });
  return createHmac('sha256', keyBytes).update(canonical).digest('base64url');
}

function proofOf(request: HttpRequest<unknown>) {
  const proof = parseSignatureHeader(request.headers.get(HEADER));
  expect(proof).not.toBeNull();
  return proof!;
}

describe('signRequest', () => {
  it('emits a header the server-side computation agrees with', async () => {
    const signed = await sign(new HttpRequest('POST', '/api/items', { name: 'طلب', count: 0 }));
    const proof = proofOf(signed.request);

    expect(proof.timestamp).toBe(String(NOW));
    expect(proof.mac).toBe(
      serverMac({ method: 'POST', path: '/items', query: '', body: '{"name":"طلب","count":0}', nonce: proof.nonce }),
    );
  });

  it('signs the service path it is given, whatever the URL was', async () => {
    const relative = await sign(new HttpRequest('GET', '/api/auth/me'), '/auth/me', 'n'.repeat(NONCE_LENGTH));
    const split = await sign(new HttpRequest('GET', 'https://api.example.test/auth/me'), '/auth/me', 'n'.repeat(NONCE_LENGTH));

    expect(relative.request.headers.get(HEADER)).toBe(split.request.headers.get(HEADER));
  });

  it('signs the parameters Angular appends, in wire form, keeping a second ?', async () => {
    const params = new HttpParams().set('search', "O'Neil").set('page', '2');
    const signed = await sign(new HttpRequest('GET', '/api/items?q=who?', { params }));
    const proof = proofOf(signed.request);

    expect(proof.mac).toBe(
      serverMac({ method: 'GET', path: '/items', query: 'q=who?&search=O%27Neil&page=2', body: '', nonce: proof.nonce }),
    );
  });

  describe('the body', () => {
    it.each([
      ['zero', 0, '0'],
      ['false', false, 'false'],
      ['an empty string', '', ''],
      ['an object', { a: [1, 2] }, '{"a":[1,2]}'],
    ])('signs and transmits %s as the exact serialisation', async (_label, body, wire) => {
      const signed = await sign(new HttpRequest('PUT', '/api/items', body));
      const proof = proofOf(signed.request);

      expect(proof.mac).toBe(serverMac({ method: 'PUT', path: '/items', query: '', body: wire, nonce: proof.nonce }));
      expect(signed.request.serializeBody()).toBe(wire);
    });

    it('hashes zero bytes for a request without a body and does not invent one', async () => {
      const signed = await sign(new HttpRequest('DELETE', '/api/items/1'), '/items/1');
      const proof = proofOf(signed.request);

      expect(signed.request.body).toBeNull();
      expect(proof.mac).toBe(serverMac({ method: 'DELETE', path: '/items/1', query: '', body: '', nonce: proof.nonce }));
    });

    it('hashes Blob, ArrayBuffer and URLSearchParams bodies over their bytes', async () => {
      const bytes = new TextEncoder().encode('raw-bytes');
      for (const body of [new Blob([bytes]), bytes.buffer, new URLSearchParams({ text: 'raw-bytes' })]) {
        const signed = await sign(new HttpRequest('POST', '/api/items', body));
        const proof = proofOf(signed.request);
        const wire = body instanceof URLSearchParams ? body.toString() : bytes;
        expect(proof.mac).toBe(serverMac({ method: 'POST', path: '/items', query: '', body: wire, nonce: proof.nonce }));
        expect(signed.request.body).toBe(body);
      }
    });

    it('marks a FormData body UNSIGNED-BODY and leaves the FormData alone', async () => {
      const form = new FormData();
      form.append('file', new Blob(['x']), 'x.txt');
      const nonce = 'f'.repeat(NONCE_LENGTH);
      const signed = await sign(new HttpRequest('POST', '/api/files', form), '/files', nonce);
      const canonical = buildRequestSigningInputBytes(TEST_PROTOCOL, {
        audience: keys.audience, method: 'POST', path: '/files', query: '', bodyHash: UNSIGNED_BODY, timestamp: String(NOW), nonce,
      });

      expect(proofOf(signed.request).mac).toBe(createHmac('sha256', keyBytes).update(canonical).digest('base64url'));
      expect(signed.request.body).toBe(form);
    });

    it('keeps the content type the original body implied, and never overrides a caller-set one', async () => {
      const inferred = await sign(new HttpRequest('POST', '/api/items', { a: 1 }));
      const explicit = await sign(
        new HttpRequest('POST', '/api/items', { a: 1 }, { headers: new HttpHeaders({ 'Content-Type': 'application/merge-patch+json' }) }),
      );

      expect(inferred.request.headers.get('Content-Type')).toBe('application/json');
      expect(explicit.request.headers.get('Content-Type')).toBe('application/merge-patch+json');
    });
  });

  describe('the nonce', () => {
    it('is a fresh CSPRNG draw of the right shape every time', () => {
      const nonces = new Set(Array.from({ length: 50 }, () => randomNonce()));
      expect(nonces.size).toBe(50);
      for (const nonce of nonces) expect(nonce).toMatch(/^[A-Za-z0-9_-]{22}$/);
    });

    it('changes the proof when the same request is signed twice', async () => {
      const request = new HttpRequest('GET', '/api/items');
      const first = await sign(request);
      const second = await sign(request);

      expect(first.nonce).not.toBe(second.nonce);
      expect(first.request.headers.get(HEADER)).not.toBe(second.request.headers.get(HEADER));
    });

    it('publishes the nonce on the request context, not on the original request', async () => {
      const request = new HttpRequest('GET', '/api/items');
      const signed = await sign(request);

      expect(signed.request.context.get(SIGNED_REQUEST_NONCE)).toBe(signed.nonce);
      expect(proofOf(signed.request).nonce).toBe(signed.nonce);
    });
  });

  it('refuses rather than signing with a clock that is not whole seconds', async () => {
    for (const nowUnixSeconds of [NOW + 0.5, -1, Number.NaN]) {
      await expect(
        signRequest(new HttpRequest('GET', '/api/items'), { protocol: TEST_PROTOCOL, servicePath: '/items', keys, nowUnixSeconds }),
      ).rejects.toBeInstanceOf(RequestNotSignedError);
    }
  });

  it('refuses a method outside the allowlist and sends nothing', async () => {
    await expect(sign(new HttpRequest('TRACE' as 'GET', '/api/items'))).rejects.toBeInstanceOf(RangeError);
  });

  it("does not rewrite the original request's headers or body", async () => {
    const original = new HttpRequest('POST', '/api/items', { a: 1 });
    await sign(original);

    expect(original.headers.has(HEADER)).toBe(false);
    expect(original.body).toEqual({ a: 1 });
  });

  it('is not an HTTP error when it refuses', () => {
    const error = new RequestNotSignedError('x');
    expect(error.reason).toBe('request_not_signed');
    expect('status' in error).toBe(false);
  });
});
