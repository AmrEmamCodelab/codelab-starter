import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { TEST_PROTOCOL_PREFIX } from '../protocol/security-protocol.ts';
import { casesOfKind } from './vector-assert.ts';
import { bodyBytes } from './vector-crypto.ts';
import {
  VECTORS_DIR_VARIABLE,
  VECTOR_PREFIX_VARIABLE,
  VectorAreas,
  expectedValues,
  loadFixtureKeys,
  loadVectorFile,
  ringKey,
  vectorSource,
  type VectorCase,
} from './vector-files.ts';

const created: string[] = [];

function folderWith(file: string, content: unknown): string {
  const directory = mkdtempSync(join(tmpdir(), 'codelab-vectors-spec-'));
  created.push(directory);
  writeFileSync(join(directory, file), JSON.stringify(content));
  return directory;
}

afterEach(() => {
  for (const directory of created.splice(0)) rmSync(directory, { recursive: true, force: true });
});

describe('vector source', () => {
  it('defaults to the checked-in set and the pinned prefix', () => {
    const source = vectorSource({});
    expect(source.prefix).toBe(TEST_PROTOCOL_PREFIX);
    expect(loadVectorFile(source, VectorAreas.canonical).prefix).toBe(TEST_PROTOCOL_PREFIX);
  });

  it('takes a folder and a prefix together, never one alone', () => {
    expect(vectorSource({ [VECTORS_DIR_VARIABLE]: 'x', [VECTOR_PREFIX_VARIABLE]: 'ALTPFX' })).toEqual({ directory: 'x', prefix: 'ALTPFX' });
    expect(() => vectorSource({ [VECTORS_DIR_VARIABLE]: 'x' })).toThrow(VECTOR_PREFIX_VARIABLE);
    expect(() => vectorSource({ [VECTOR_PREFIX_VARIABLE]: 'ALTPFX' })).toThrow(VECTORS_DIR_VARIABLE);
  });
});

describe('vector files', () => {
  const header = { schemaVersion: 1, area: 'canonical', prefix: 'ALTPFX', fixtureKeyId: 'none' };
  const oneCase = [{ name: 'a', kind: 'frame', expectedCanonicalBase64: 'AA==' }];

  it('refuses a file whose prefix is not the run prefix', () => {
    const directory = folderWith('canonical.vectors.json', { ...header, cases: oneCase });
    expect(() => loadVectorFile({ directory, prefix: 'OTHER' }, VectorAreas.canonical)).toThrow('prefix');
    expect(loadVectorFile({ directory, prefix: 'ALTPFX' }, VectorAreas.canonical).cases).toHaveLength(1);
  });

  it('refuses another schema version, another area and an empty case list', () => {
    const source = (content: unknown) => ({ directory: folderWith('canonical.vectors.json', content), prefix: 'ALTPFX' });
    expect(() => loadVectorFile(source({ ...header, schemaVersion: 2, cases: oneCase }), VectorAreas.canonical)).toThrow('schemaVersion');
    expect(() => loadVectorFile(source({ ...header, area: 'public-id', cases: oneCase }), VectorAreas.canonical)).toThrow('area');
    expect(() => loadVectorFile(source({ ...header, cases: [] }), VectorAreas.canonical)).toThrow('no cases');
  });

  it('reads expected values and selects cases by kind, refusing an empty selection', () => {
    const cases = [{ name: 'a', kind: 'sign', expectedX: '1', expectedCount: 3, input: '2' }] as VectorCase[];
    expect(expectedValues(cases[0])).toEqual({ expectedX: '1' });
    expect(casesOfKind(cases, 'sign')).toHaveLength(1);
    expect(() => casesOfKind(cases, 'verify')).toThrow('verify');
  });
});

describe('vector bodies and fixture keys', () => {
  it('decodes utf8, base64 and repeated bodies to exact bytes', () => {
    expect(Array.from(bodyBytes({ encoding: 'utf8', text: 'é' }))).toEqual([0xc3, 0xa9]);
    expect(Array.from(bodyBytes({ encoding: 'base64', base64: 'AAEC' }))).toEqual([0, 1, 2]);
    expect(bodyBytes({ encoding: 'repeat', text: 'ab', count: 3 }).length).toBe(6);
  });

  it('loads TEST-ONLY rings and refuses unknown key ids', () => {
    const keys = loadFixtureKeys();
    expect(ringKey(keys.signing, keys.signing.activeKeyId)).toHaveLength(32);
    expect(() => ringKey(keys.signing, 'absent')).toThrow('absent');
  });

  it('refuses a fixture file without the TEST-ONLY label', () => {
    const directory = folderWith('signing.json', { ring: { activeKeyId: 'a', keys: [] } });
    expect(() => loadFixtureKeys(directory)).toThrow('TEST-ONLY');
  });
});
