import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, describe, expect, it } from 'vitest';
import {
  SUPPORTED_SCHEMA_VERSION,
  featureFolderOf,
  featurePolicies,
  findCompiledRules,
  loadCompiledRules,
} from '../src/eslint/compiled-rules.mjs';
import { basePolicies, elementDescriptors } from '../src/eslint/element-types.mjs';

const FIXTURE = fileURLToPath(new URL('./fixtures/boundary-app', import.meta.url));
const REPO_ROOT = fileURLToPath(new URL('../../../../', import.meta.url));
const temporary = [];

function tempFile(content) {
  const folder = mkdtempSync(join(tmpdir(), 'ng-tools-rules-'));
  temporary.push(folder);
  const path = join(folder, 'compiled.json');
  writeFileSync(path, content);
  return path;
}

afterEach(() => {
  for (const folder of temporary.splice(0)) rmSync(folder, { recursive: true, force: true });
});

describe('featureFolderOf', () => {
  it('returns the folder directly under features/', () => {
    expect(featureFolderOf('frontend/starter-web/src/app/features/records')).toBe('records');
    expect(featureFolderOf('src\\app\\features\\orders\\')).toBe('orders');
    expect(featureFolderOf('features/records')).toBe('records');
  });

  it('returns null for anything that is not one feature folder', () => {
    for (const path of ['backend/src/Modules/Records', 'src/app/features', 'src/app/features/records/list', 'src/app/core']) {
      expect(featureFolderOf(path)).toBeNull();
    }
  });
});

describe('loadCompiledRules', () => {
  it('reads the fixture and the repository file', () => {
    expect(loadCompiledRules(join(FIXTURE, 'rules', 'eslint-boundaries.compiled.json')).modules).toHaveLength(4);
    expect(loadCompiledRules(join(REPO_ROOT, 'rules', 'eslint-boundaries.compiled.json')).schemaVersion).toBe(SUPPORTED_SCHEMA_VERSION);
  });

  it('refuses an unsupported schema version and a missing module list', () => {
    expect(() => loadCompiledRules(tempFile('{"schemaVersion":2,"modules":[]}'))).toThrow(/schemaVersion 2/);
    expect(() => loadCompiledRules(tempFile('{"schemaVersion":1}'))).toThrow(/modules/);
    expect(() => loadCompiledRules(tempFile('not json'))).toThrow(SyntaxError);
  });
});

describe('findCompiledRules', () => {
  it('walks up to the nearest rules folder, or returns null', () => {
    expect(findCompiledRules(join(FIXTURE, 'src', 'app', 'features'))).toBe(join(FIXTURE, 'rules', 'eslint-boundaries.compiled.json'));
    expect(findCompiledRules(tmpdir())).toBeNull();
  });
});

describe('featurePolicies', () => {
  const compiled = loadCompiledRules(join(FIXTURE, 'rules', 'eslint-boundaries.compiled.json'));

  it('turns mayDependOn into allow and mustNotDependOn into disallow, for angular feature modules only', () => {
    expect(featurePolicies(compiled)).toEqual([
      {
        from: { element: { type: 'features', captured: { feature: 'records' } } },
        allow: { to: { element: { type: 'features', captured: [{ feature: 'orders' }] } } },
      },
      {
        from: { element: { type: 'features', captured: { feature: 'records' } } },
        disallow: { to: { element: { type: 'features', captured: [{ feature: 'reports' }] } } },
      },
    ]);
  });

  it('ignores dependencies on modules that are not features', () => {
    const modules = [{ module: 'A', kind: 'angular', path: 'src/app/features/a', mayDependOn: ['Backend', 'Missing'] }];
    expect(featurePolicies({ modules })).toEqual([]);
  });

  it('produces nothing for the current repository (no frontend modules yet)', () => {
    expect(featurePolicies(loadCompiledRules(join(REPO_ROOT, 'rules', 'eslint-boundaries.compiled.json')))).toEqual([]);
  });
});

describe('elementDescriptors and basePolicies', () => {
  it('anchors every element at the app root with full-path matching', () => {
    const descriptors = elementDescriptors('src/app/');
    expect(descriptors.map((descriptor) => descriptor.pattern)).toEqual([
      'src/app/core', 'src/app/layouts', 'src/app/patterns', 'src/app/shared', 'src/app/showcase', 'src/app/features/*',
    ]);
    expect(descriptors.every((descriptor) => descriptor.partialMatch === false)).toBe(true);
    expect(elementDescriptors('src\\app')[0].pattern).toBe('src/app/core');
    expect(elementDescriptors('')[0].pattern).toBe('core');
  });

  it('gives core no allow policy at all', () => {
    expect(basePolicies().some((policy) => policy.from.element.type === 'core')).toBe(false);
  });
});
