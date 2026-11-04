import tsParser from '@typescript-eslint/parser';
import { RuleTester } from 'eslint';
import { afterAll, describe, expect, it } from 'vitest';
import { isDecoderFile, wireIdCastRule } from '../src/eslint/rules/wire-id-cast.mjs';

RuleTester.describe = describe;
RuleTester.it = it;
RuleTester.afterAll = afterAll;

const tester = new RuleTester({ languageOptions: { parser: tsParser, sourceType: 'module' } });
const FEATURE_FILE = 'src/app/features/records/record-page.ts';
const cast = (name) => ({ messageId: 'cast', data: { name } });

tester.run('codelab/wire-id-cast', wireIdCastRule, {
  valid: [
    { filename: FEATURE_FILE, code: "const id = tryAsId('record', raw);" },
    { filename: FEATURE_FILE, code: "const id: WireId<'record'> = asId('record', raw);" },
    { filename: FEATURE_FILE, code: 'const text = id as string;' },
    { filename: FEATURE_FILE, code: 'const value = raw as RecordId;' },
    { filename: FEATURE_FILE, code: "type RecordId = WireId<'record'>; function f(id: RecordId) { return id; }" },
    { filename: 'packages/npm/ng-security/src/wire-id/wire-id.ts', code: 'return raw as WireId<D>;' },
    { filename: 'src/app/core/ids/wire-id.ts', code: "const id = raw as WireId<'record'>;", options: [{}] },
  ],
  invalid: [
    { filename: FEATURE_FILE, code: "const id = raw as WireId<'record'>;", errors: [cast('WireId')] },
    { filename: FEATURE_FILE, code: "const id = <WireId<'record'>>raw;", errors: [cast('WireId')] },
    { filename: FEATURE_FILE, code: "const id = raw as unknown as WireId<'record'>;", errors: [cast('WireId')] },
    { filename: FEATURE_FILE, code: "const id = raw as security.WireId<'record'>;", errors: [cast('WireId')] },
    {
      filename: FEATURE_FILE,
      code: "export type RecordId = WireId<'record'>;\ntype Alias = RecordId;\nconst a = raw as RecordId;\nconst b = raw as Alias;",
      errors: [cast('RecordId'), cast('Alias')],
    },
    { filename: FEATURE_FILE, code: 'const id = raw as RecordId;', options: [{ aliases: ['RecordId'] }], errors: [cast('RecordId')] },
    {
      filename: FEATURE_FILE,
      code: "const id = raw as Opaque<'record'>;",
      options: [{ brandedType: 'Opaque' }],
      errors: [cast('Opaque')],
    },
    {
      filename: 'src/app/core/ids/wire-id.ts',
      code: "const id = raw as WireId<'record'>;",
      options: [{ decoderFiles: ['core/ids/decoder.ts'] }],
      errors: [cast('WireId')],
    },
  ],
});

describe('isDecoderFile', () => {
  it('matches whole trailing path segments on either separator', () => {
    expect(isDecoderFile('C:\\repo\\src\\wire-id.ts', ['wire-id.ts'])).toBe(true);
    expect(isDecoderFile('/repo/src/ids/wire-id.ts', ['ids/wire-id.ts'])).toBe(true);
    expect(isDecoderFile('/repo/src/my-wire-id.ts', ['wire-id.ts'])).toBe(false);
    expect(isDecoderFile('/repo/src/wire-id.ts.bak', ['wire-id.ts'])).toBe(false);
  });
});
