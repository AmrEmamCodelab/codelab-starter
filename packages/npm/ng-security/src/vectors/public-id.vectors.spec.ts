import { describe, expect, it } from 'vitest';
import { createSecurityProtocol } from '../protocol/security-protocol.ts';
import { isWireId } from '../wire-id/wire-id.ts';
import { casesOfKind, expectProduced } from './vector-assert.ts';
import { PublicIdPurposes, openPublicId, protectPublicId, publicIdAad, publicIdKdfInfo } from './vector-crypto.ts';
import { bytesToBase64, loadFixtureKeys, loadVectorFile, text, vectorSource, VectorAreas } from './vector-files.ts';

const source = vectorSource();
const protocol = createSecurityProtocol(source.prefix);
const file = loadVectorFile(source, VectorAreas.publicId);
const ring = loadFixtureKeys().publicIds;
const Outcomes = Object.freeze({ accepted: 'accepted', refused: 'refused' } as const);

describe(`public-id vectors (prefix ${source.prefix})`, () => {
  it.each(casesOfKind(file.cases, 'protect'))('mints $name byte for byte', async (vectorCase) => {
    const keyId = text(vectorCase, 'keyId');
    const domain = text(vectorCase, 'domain');
    const token = await protectPublicId(protocol, ring, { keyId, domain, id: BigInt(text(vectorCase, 'id')) });
    expect(isWireId(token)).toBe(true);
    expectProduced(vectorCase, {
      expectedAadCanonicalBase64: bytesToBase64(publicIdAad(protocol, keyId, domain)),
      expectedEncryptionKdfInfoCanonicalBase64: bytesToBase64(publicIdKdfInfo(protocol, keyId, PublicIdPurposes.encryption)),
      expectedMacKdfInfoCanonicalBase64: bytesToBase64(publicIdKdfInfo(protocol, keyId, PublicIdPurposes.mac)),
      expectedToken: token,
    });
  });

  it.each(casesOfKind(file.cases, 'open'))('opens $name with the same outcome', async (vectorCase) => {
    const id = await openPublicId(protocol, ring, { domain: text(vectorCase, 'domain'), value: text(vectorCase, 'token') });
    expectProduced(vectorCase, {
      expectedOutcome: id === null ? Outcomes.refused : Outcomes.accepted,
      expectedId: id === null ? '' : id.toString(),
    });
  });
});
