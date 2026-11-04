import { describe, it } from 'vitest';
import { createSecurityProtocol } from '../protocol/security-protocol.ts';
import { buildResponseSigningInputBytes, computeMac } from '../signing/signature-canonical.ts';
import { formatResponseSignatureHeader } from '../signing/signature-header.ts';
import {
  ResponseSignatureFailures,
  isResponseSignatureError,
  verifyResponseSignature,
} from '../verification/response-signature.ts';
import { CaseKinds, Verdicts, caseBody, caseBodyHash, caseSessionKeys, type Verdict } from './signing-cases.ts';
import { casesOfKind, expectProduced } from './vector-assert.ts';
import { bodyBytes } from './vector-crypto.ts';
import { bytesToBase64, integer, loadFixtureKeys, loadVectorFile, text, vectorSource, VectorAreas, type VectorCase } from './vector-files.ts';

const source = vectorSource();
const protocol = createSecurityProtocol(source.prefix);
const file = loadVectorFile(source, VectorAreas.responseSignature);
const ring = loadFixtureKeys().signing;

/** How the browser judges a response carrying `header`: the package's own verifier, its error mapped to a verdict. */
async function browserVerdict(vectorCase: VectorCase, header: string): Promise<Verdict> {
  const keys = await caseSessionKeys(protocol, ring, vectorCase);
  const bytes = bodyBytes(caseBody(vectorCase));
  try {
    await verifyResponseSignature(
      { protocol, key: keys.verify, requestNonce: text(vectorCase, 'requestNonce') },
      { status: integer(vectorCase, 'status'), contentType: text(vectorCase, 'contentType'), body: bytes.buffer, signatureHeader: header, url: null },
    );
    return Verdicts.valid;
  } catch (error) {
    if (!isResponseSignatureError(error)) throw error;
    return error.reason === ResponseSignatureFailures.malformed ? Verdicts.malformed : Verdicts.mismatch;
  }
}

describe(`response-signature vectors (prefix ${source.prefix})`, () => {
  const signCases = casesOfKind(file.cases, CaseKinds.sign);

  it.each(signCases)('signs $name byte for byte', async (vectorCase) => {
    const keys = await caseSessionKeys(protocol, ring, vectorCase);
    const bodyHash = await caseBodyHash(vectorCase);
    const canonical = buildResponseSigningInputBytes(protocol, {
      requestNonce: text(vectorCase, 'requestNonce'),
      status: integer(vectorCase, 'status'),
      contentType: text(vectorCase, 'contentType'),
      bodyHash,
    });
    const mac = await computeMac(keys.sign, canonical);
    expectProduced(vectorCase, {
      expectedBodyHash: bodyHash,
      expectedCanonicalBase64: bytesToBase64(canonical),
      expectedSignature: mac,
      expectedHeader: formatResponseSignatureHeader(mac),
    });
  });

  it.each(signCases)('the browser verifier accepts the signed $name', async (vectorCase) => {
    expectProduced({ name: vectorCase.name, kind: vectorCase.kind, expectedVerdict: Verdicts.valid }, {
      expectedVerdict: await browserVerdict(vectorCase, text(vectorCase, 'expectedHeader')),
    });
  });

  it.each(casesOfKind(file.cases, CaseKinds.verify))('judges $name like the server', async (vectorCase) => {
    expectProduced(vectorCase, { expectedVerdict: await browserVerdict(vectorCase, text(vectorCase, 'presentedHeader')) });
  });
});
