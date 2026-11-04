import { beforeAll, describe, expect, it } from 'vitest';
import { TEST_PROTOCOL, bufferOf, randomKeyBytes, sessionKeysFrom } from '../testing/test-support.ts';
import type { SessionSigningKeys } from '../session/session-keys.store.ts';
import { BASE64URL_ALPHABET, base64UrlEncode } from '../signing/base64url.ts';
import { UNSIGNED_BODY, buildResponseSigningInputBytes, computeMac, hashBody } from '../signing/signature-canonical.ts';
import { formatResponseSignatureHeader } from '../signing/signature-header.ts';
import { SignatureProblemCodes } from '../signing/signature-problem-codes.ts';
import {
  ResponseSignatureError,
  ResponseSignatureFailures,
  isResponseSignatureError,
  mayOmitResponseSignature,
  verifyResponseSignature,
  type ReceivedResponse,
} from './response-signature.ts';

const NONCE = 'r'.repeat(22);
const JSON_TYPE = 'application/json; charset=utf-8';
let keys: SessionSigningKeys;
let otherKeys: SessionSigningKeys;

beforeAll(async () => {
  keys = await sessionKeysFrom(randomKeyBytes());
  otherKeys = await sessionKeysFrom(randomKeyBytes());
});

/** Signs a response the way the server does, with `signer`'s sign handle. */
async function serverSigned(
  response: Omit<ReceivedResponse, 'signatureHeader' | 'url'>,
  options: { signer?: SessionSigningKeys; nonce?: string; bodyHash?: string } = {},
): Promise<ReceivedResponse> {
  const canonical = buildResponseSigningInputBytes(TEST_PROTOCOL, {
    requestNonce: options.nonce ?? NONCE,
    status: response.status,
    contentType: response.contentType,
    bodyHash: options.bodyHash ?? (await hashBody(response.body ?? new ArrayBuffer(0))),
  });
  const mac = await computeMac((options.signer ?? keys).sign, canonical);
  return { ...response, signatureHeader: formatResponseSignatureHeader(mac), url: '/items' };
}

function verify(response: ReceivedResponse) {
  return verifyResponseSignature({ protocol: TEST_PROTOCOL, key: keys.verify, requestNonce: NONCE }, response);
}

async function expectFailure(response: ReceivedResponse, reason: string) {
  await expect(verify(response)).rejects.toMatchObject({ name: 'ResponseSignatureError', reason });
}

const OK_BODY = { status: 200, contentType: JSON_TYPE, body: bufferOf('{"id":"x"}') };

describe('verifyResponseSignature', () => {
  it('accepts a response signed over the exact bytes received', async () => {
    await expect(verify(await serverSigned(OK_BODY))).resolves.toBeUndefined();
  });

  it('accepts a signed response with no body (the digest of zero bytes)', async () => {
    await expect(verify(await serverSigned({ status: 200, contentType: '', body: null }))).resolves.toBeUndefined();
  });

  describe('refuses', () => {
    it('a tampered body', async () => {
      const signed = await serverSigned(OK_BODY);
      await expectFailure({ ...signed, body: bufferOf('{"id":"y"}') }, ResponseSignatureFailures.mismatch);
    });

    it('a response signed with a different key', async () => {
      await expectFailure(await serverSigned(OK_BODY, { signer: otherKeys }), ResponseSignatureFailures.mismatch);
    });

    it('a 200 that carries no signature header at all', async () => {
      await expectFailure({ ...OK_BODY, signatureHeader: null, url: null }, ResponseSignatureFailures.missing);
    });

    it('a malformed header', async () => {
      for (const header of ['1.short', `2.${'A'.repeat(43)}`, '']) {
        await expectFailure({ ...OK_BODY, signatureHeader: header, url: null }, ResponseSignatureFailures.malformed);
      }
    });

    it('a MAC with non-zero slack bits in its final character', async () => {
      const signed = await serverSigned(OK_BODY);
      const header = signed.signatureHeader!;
      const index = BASE64URL_ALPHABET.indexOf(header[header.length - 1]);
      const variant = header.slice(0, -1) + BASE64URL_ALPHABET[index ^ 0x01];
      await expectFailure({ ...signed, signatureHeader: variant }, ResponseSignatureFailures.malformed);
    });

    it('a response signed against another request nonce', async () => {
      await expectFailure(await serverSigned(OK_BODY, { nonce: 'o'.repeat(22) }), ResponseSignatureFailures.mismatch);
    });

    it('a body whose status or content type was substituted', async () => {
      const signed = await serverSigned(OK_BODY);
      await expectFailure({ ...signed, status: 201 }, ResponseSignatureFailures.mismatch);
      await expectFailure({ ...signed, contentType: 'text/html' }, ResponseSignatureFailures.mismatch);
    });

    it('a 204 or an error that carries a header which does not verify', async () => {
      const wrong = formatResponseSignatureHeader(base64UrlEncode(new Uint8Array(32)));
      await expectFailure({ status: 204, contentType: '', body: null, signatureHeader: wrong, url: null }, ResponseSignatureFailures.mismatch);
      await expectFailure({ status: 403, contentType: JSON_TYPE, body: bufferOf('{}'), signatureHeader: wrong, url: null }, ResponseSignatureFailures.mismatch);
    });
  });

  describe('the exemptions, a closed list rather than a URL list', () => {
    it('lets 204, 304, sub-200 and every error status omit the header, and no other 2xx or 3xx', () => {
      for (const status of [0, 101, 204, 304, 400, 401, 403, 429, 500, 503]) expect(mayOmitResponseSignature(status)).toBe(true);
      for (const status of [200, 201, 202, 206, 301, 302]) expect(mayOmitResponseSignature(status)).toBe(false);
    });

    it('accepts an error with no header, and verifies one that carries a header', async () => {
      const problem = { status: 401, contentType: 'application/problem+json', body: bufferOf('{"code":"signature_stale"}') };
      await expect(verify({ ...problem, signatureHeader: null, url: null })).resolves.toBeUndefined();
      await expect(verify(await serverSigned(problem))).resolves.toBeUndefined();
    });
  });

  describe('the UNSIGNED-BODY form', () => {
    it('accepts a response the server signed with the marker instead of a digest', async () => {
      await expect(verify(await serverSigned(OK_BODY, { bodyHash: UNSIGNED_BODY }))).resolves.toBeUndefined();
    });

    it('does not cover the body bytes of such a response, but still covers status and type', async () => {
      const signed = await serverSigned(OK_BODY, { bodyHash: UNSIGNED_BODY });
      await expect(verify({ ...signed, body: bufferOf('other') })).resolves.toBeUndefined();
      await expectFailure({ ...signed, status: 202 }, ResponseSignatureFailures.mismatch);
    });
  });

  it('does not turn a key without verify usage into a mismatch', async () => {
    const signed = await serverSigned(OK_BODY);
    await expect(
      verifyResponseSignature({ protocol: TEST_PROTOCOL, key: keys.sign, requestNonce: NONCE }, signed),
    ).rejects.not.toBeInstanceOf(ResponseSignatureError);
  });
});

describe('ResponseSignatureError', () => {
  it('is not an HTTP error response and carries the status and URL for diagnosis', () => {
    const error = new ResponseSignatureError(ResponseSignatureFailures.mismatch, { status: 200, url: '/items' });

    expect(error).toBeInstanceOf(Error);
    expect(error.constructor.name).toBe('ResponseSignatureError');
    expect(error.status).toBe(200);
    expect(error.url).toBe('/items');
    expect(error.message).toContain('response_signature_mismatch');
    expect(error.message).toContain('/items');
  });

  it('is recognised across a bundle boundary, where instanceof can fail', () => {
    const foreign = Object.assign(new Error('x'), { name: 'ResponseSignatureError' });
    expect(isResponseSignatureError(foreign)).toBe(true);
    expect(isResponseSignatureError(new Error('x'))).toBe(false);
    expect(isResponseSignatureError(null)).toBe(false);
  });

  it('publishes a closed failure vocabulary distinct from the server problem codes', () => {
    expect(Object.values(ResponseSignatureFailures)).toHaveLength(3);
    const server = new Set<string>(Object.values(SignatureProblemCodes));
    for (const reason of Object.values(ResponseSignatureFailures)) expect(server.has(reason)).toBe(false);
  });
});
