import { describe, it } from 'vitest';
import { createSecurityProtocol } from '../protocol/security-protocol.ts';
import { casesOfKind, expectProduced } from './vector-assert.ts';
import { hkdf, sessionKeyInfo } from './vector-crypto.ts';
import { bytesToBase64, loadFixtureKeys, loadVectorFile, ringKey, text, vectorSource, VectorAreas } from './vector-files.ts';

const source = vectorSource();
const protocol = createSecurityProtocol(source.prefix);
const file = loadVectorFile(source, VectorAreas.sessionKeys);
const keys = loadFixtureKeys();

describe(`session-key vectors (prefix ${source.prefix})`, () => {
  it.each(casesOfKind(file.cases, 'derive'))('derives $name', async (vectorCase) => {
    const info = sessionKeyInfo(protocol, { userId: text(vectorCase, 'userId'), sessionId: text(vectorCase, 'sessionId') });
    const key = await hkdf(ringKey(keys.signing, text(vectorCase, 'masterKeyId')), info);
    expectProduced(vectorCase, {
      expectedKdfInfoCanonicalBase64: bytesToBase64(info),
      expectedSessionKeyBase64: bytesToBase64(key),
    });
  });
});
