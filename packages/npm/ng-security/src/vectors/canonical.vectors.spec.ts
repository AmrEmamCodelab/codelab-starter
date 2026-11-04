import { describe, it } from 'vitest';
import { buildCanonicalBytes } from '../canonical/canonical.ts';
import { createSecurityProtocol } from '../protocol/security-protocol.ts';
import { casesOfKind, expectProduced } from './vector-assert.ts';
import { bytesToBase64, loadVectorFile, text, vectorSource, VectorAreas, type VectorCase } from './vector-files.ts';

const source = vectorSource();
const protocol = createSecurityProtocol(source.prefix);
const file = loadVectorFile(source, VectorAreas.canonical);

function fieldsOf(vectorCase: VectorCase): string[] {
  const fields = vectorCase['fields'];
  if (!Array.isArray(fields)) throw new Error(`Vector case '${vectorCase.name}': 'fields' is not an array.`);
  return fields as string[];
}

describe(`canonical vectors (prefix ${source.prefix})`, () => {
  it.each(casesOfKind(file.cases, 'frame'))('frames $name byte for byte', (vectorCase) => {
    const bytes = buildCanonicalBytes(protocol, text(vectorCase, 'profile'), fieldsOf(vectorCase));
    expectProduced(vectorCase, { expectedCanonicalBase64: bytesToBase64(bytes) });
  });
});
