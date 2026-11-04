import { HttpRequest } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import type { CryptoBytes } from '../canonical/canonical.ts';
import { createSecurityProtocol } from '../protocol/security-protocol.ts';
import { decodeBase64UrlStrict } from '../signing/base64url.ts';
import { signRequest } from '../signing/request-signature.ts';
import { buildRequestSigningInputBytes, computeMac, verifyMac } from '../signing/signature-canonical.ts';
import { MAC_BYTES, formatSignatureHeader, parseSignatureHeader } from '../signing/signature-header.ts';
import { CaseKinds, Verdicts, caseBody, caseBodyHash, caseSessionKeys, type Verdict } from './signing-cases.ts';
import { casesOfKind, expectProduced } from './vector-assert.ts';
import { bodyBytes } from './vector-crypto.ts';
import { bytesToBase64, flag, loadFixtureKeys, loadVectorFile, text, vectorSource, VectorAreas, type VectorCase } from './vector-files.ts';

const source = vectorSource();
const protocol = createSecurityProtocol(source.prefix);
const file = loadVectorFile(source, VectorAreas.requestSignature);
const ring = loadFixtureKeys().signing;
const API_ROOT = 'https://api.example.test/api';
const QUERY_MARK = '?';
const UPLOAD_FIELD = 'file';

function canonicalOf(vectorCase: VectorCase, proof: { bodyHash: string; timestamp: string; nonce: string }): CryptoBytes {
  return buildRequestSigningInputBytes(protocol, {
    audience: text(vectorCase, 'audience'),
    method: text(vectorCase, 'method'),
    path: text(vectorCase, 'path'),
    query: text(vectorCase, 'query'),
    ...proof,
  });
}

/** The request an Angular app would send for the case: a string, an ArrayBuffer, a FormData or no body. */
function httpRequestOf(vectorCase: VectorCase): HttpRequest<unknown> {
  const query = text(vectorCase, 'query');
  const url = API_ROOT + text(vectorCase, 'path') + (query === '' ? '' : QUERY_MARK + query);
  return new HttpRequest<unknown>(text(vectorCase, 'method'), url, requestBody(vectorCase));
}

function requestBody(vectorCase: VectorCase): unknown {
  if (flag(vectorCase, 'bodyUnsigned')) {
    const form = new FormData();
    form.append(UPLOAD_FIELD, 'bytes the browser frames itself');
    return form;
  }
  const body = caseBody(vectorCase);
  const bytes = bodyBytes(body);
  if (bytes.length === 0) return null;
  return body.encoding === 'base64' ? bytes.buffer : new TextDecoder().decode(bytes);
}

async function verdictOf(vectorCase: VectorCase): Promise<Verdict> {
  const header = parseSignatureHeader(text(vectorCase, 'presentedHeader'));
  const mac = header === null ? null : decodeBase64UrlStrict(header.mac, MAC_BYTES);
  if (header === null || mac === null) return Verdicts.malformed;
  const keys = await caseSessionKeys(protocol, ring, vectorCase);
  const canonical = canonicalOf(vectorCase, { bodyHash: await caseBodyHash(vectorCase), timestamp: header.timestamp, nonce: header.nonce });
  return (await verifyMac(keys.verify, mac, canonical)) ? Verdicts.valid : Verdicts.mismatch;
}

describe(`request-signature vectors (prefix ${source.prefix})`, () => {
  const signCases = casesOfKind(file.cases, CaseKinds.sign);

  it.each(signCases)('signs $name byte for byte', async (vectorCase) => {
    const keys = await caseSessionKeys(protocol, ring, vectorCase);
    const timestamp = text(vectorCase, 'timestamp');
    const nonce = text(vectorCase, 'nonce');
    const bodyHash = await caseBodyHash(vectorCase);
    const canonical = canonicalOf(vectorCase, { bodyHash, timestamp, nonce });
    const mac = await computeMac(keys.sign, canonical);
    expectProduced(vectorCase, {
      expectedBodyHash: bodyHash,
      expectedCanonicalBase64: bytesToBase64(canonical),
      expectedSignature: mac,
      expectedHeader: formatSignatureHeader({ timestamp, nonce, mac }) ?? '',
    });
  });

  it.each(signCases)('the interceptor signer sends the expected header for $name', async (vectorCase) => {
    const keys = await caseSessionKeys(protocol, ring, vectorCase);
    const signed = await signRequest(httpRequestOf(vectorCase), {
      protocol,
      servicePath: text(vectorCase, 'path'),
      keys,
      nowUnixSeconds: Number(text(vectorCase, 'timestamp')),
      nonce: text(vectorCase, 'nonce'),
    });
    expect(signed.request.headers.get(protocol.signatureHeader)).toBe(text(vectorCase, 'expectedHeader'));
  });

  it.each(casesOfKind(file.cases, CaseKinds.verify))('judges $name', async (vectorCase) => {
    expectProduced(vectorCase, { expectedVerdict: await verdictOf(vectorCase) });
  });
});
