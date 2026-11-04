import { createHash, createHmac } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { TEST_PROTOCOL, randomKeyBytes, sessionKeysFrom } from '../testing/test-support.ts';
import { decodeBase64UrlStrict } from './base64url.ts';
import {
  UNSIGNED_BODY,
  buildRequestSigningInput,
  buildRequestSigningInputBytes,
  buildResponseSigningInput,
  buildResponseSigningInputBytes,
  computeMac,
  hashBody,
  verifyMac,
  type SignedRequestFields,
} from './signature-canonical.ts';

const REQUEST: SignedRequestFields = {
  audience: 'Starter.Api',
  method: 'post',
  path: '/items/%7e1',
  query: 'a=1',
  bodyHash: UNSIGNED_BODY,
  timestamp: '987654321',
  nonce: 'n'.repeat(22),
};

describe('request canonical form', () => {
  it('frames the scheme, the allowlist method literal and the normalised path, in order', () => {
    expect(buildRequestSigningInput(TEST_PROTOCOL, REQUEST)).toBe(
      '20:CODELABTEST-CANON-v1\n22:CODELABTEST-SIG-v1/REQ\n1:8\n' +
        '18:CODELABTEST-SIG-v1\n11:Starter.Api\n4:POST\n9:/items/~1\n3:a=1\n' +
        '13:UNSIGNED-BODY\n9:987654321\n22:' + 'n'.repeat(22) + '\n',
    );
  });

  it('emits the UTF-8 bytes of the same form', () => {
    expect(new TextDecoder().decode(buildRequestSigningInputBytes(TEST_PROTOCOL, REQUEST))).toBe(
      buildRequestSigningInput(TEST_PROTOCOL, REQUEST),
    );
  });

  it('throws on a method outside the allowlist', () => {
    expect(() => buildRequestSigningInput(TEST_PROTOCOL, { ...REQUEST, method: 'TRACE' })).toThrow(RangeError);
  });

  it('changes when any covered field changes', () => {
    const base = buildRequestSigningInput(TEST_PROTOCOL, REQUEST);
    for (const key of Object.keys(REQUEST) as (keyof SignedRequestFields)[]) {
      const changed = { ...REQUEST, [key]: key === 'method' ? 'PUT' : `${REQUEST[key]}x` };
      expect(buildRequestSigningInput(TEST_PROTOCOL, changed)).not.toBe(base);
    }
  });
});

describe('response canonical form', () => {
  const RESPONSE = { requestNonce: 'n'.repeat(22), status: 200, contentType: 'application/json', bodyHash: 'h' };

  it('frames the scheme, the request nonce, the status digits, the content type and the body hash', () => {
    expect(buildResponseSigningInput(TEST_PROTOCOL, RESPONSE)).toBe(
      '20:CODELABTEST-CANON-v1\n23:CODELABTEST-SIG-v1/RESP\n1:5\n' +
        '18:CODELABTEST-SIG-v1\n22:' + 'n'.repeat(22) + '\n3:200\n16:application/json\n1:h\n',
    );
    expect(new TextDecoder().decode(buildResponseSigningInputBytes(TEST_PROTOCOL, RESPONSE))).toBe(
      buildResponseSigningInput(TEST_PROTOCOL, RESPONSE),
    );
  });

  it('cannot be confused with a request form over the same values', () => {
    expect(buildResponseSigningInput(TEST_PROTOCOL, RESPONSE)).not.toContain('SIG-v1/REQ');
  });

  it.each([200.5, Number.NaN, Number.POSITIVE_INFINITY])('throws on the non-integer status %s', (status) => {
    expect(() => buildResponseSigningInput(TEST_PROTOCOL, { ...RESPONSE, status })).toThrow(TypeError);
  });
});

describe('digests', () => {
  it('hashes bytes to unpadded base64url SHA-256, zero bytes included', async () => {
    for (const body of [new Uint8Array(0), new TextEncoder().encode('{"a":1}')]) {
      expect(await hashBody(body)).toBe(createHash('sha256').update(body).digest('base64url'));
    }
    expect(await hashBody(new Uint8Array(0))).toHaveLength(43);
  });

  it('computes the HMAC-SHA256 an independent implementation computes', async () => {
    const bytes = randomKeyBytes();
    const keys = await sessionKeysFrom(bytes);
    const canonical = buildRequestSigningInputBytes(TEST_PROTOCOL, REQUEST);

    const mac = await computeMac(keys.sign, canonical);

    expect(mac).toBe(createHmac('sha256', bytes).update(canonical).digest('base64url'));
  });

  it('verifies its own MAC with the verify handle and refuses a different key', async () => {
    const keys = await sessionKeysFrom(randomKeyBytes());
    const other = await sessionKeysFrom(randomKeyBytes());
    const canonical = buildRequestSigningInputBytes(TEST_PROTOCOL, REQUEST);
    const mac = decodeBase64UrlStrict(await computeMac(keys.sign, canonical), 32);

    expect(mac).not.toBeNull();
    expect(await verifyMac(keys.verify, mac!, canonical)).toBe(true);
    expect(await verifyMac(other.verify, mac!, canonical)).toBe(false);
  });
});
