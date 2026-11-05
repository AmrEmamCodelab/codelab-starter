// @vitest-environment node
import ts from 'typescript';
import { describe, expect, it } from 'vitest';
import { PUBLIC_ID_PATTERN } from '../lib/constants.mjs';
import {
  WIRE_ID_IMPORT_LINE,
  createWireIdTransform,
  isNullableSchema,
  isPublicIdSchema,
  pointerSegments,
  stripSchemaSuffix,
  toKebabCase,
  wireIdDomain,
} from '../lib/wire-id.mjs';

const publicId = (extra = {}) => ({ type: 'string', pattern: PUBLIC_ID_PATTERN, ...extra });

function print(node) {
  const file = ts.createSourceFile('probe.ts', '', ts.ScriptTarget.Latest, false, ts.ScriptKind.TS);
  return ts.createPrinter().printNode(ts.EmitHint.Unspecified, node, file);
}

describe('isPublicIdSchema', () => {
  it('recognises the Codelab.Security PublicId shape, nullable or not', () => {
    expect(isPublicIdSchema(publicId())).toBe(true);
    expect(isPublicIdSchema(publicId({ type: ['string', 'null'] }))).toBe(true);
  });

  it.each([
    [{ type: 'string' }],
    [{ type: 'string', pattern: '^[A-Z]+$' }],
    [{ type: 'integer', pattern: PUBLIC_ID_PATTERN }],
    [null],
    [undefined],
  ])('does not treat %j as a PublicId', (schema) => {
    expect(isPublicIdSchema(schema)).toBe(false);
  });
});

describe('isNullableSchema', () => {
  it('reads OpenAPI 3.1 type arrays and 3.0 nullable', () => {
    expect(isNullableSchema(publicId({ type: ['string', 'null'] }))).toBe(true);
    expect(isNullableSchema(publicId({ nullable: true }))).toBe(true);
    expect(isNullableSchema(publicId())).toBe(false);
  });
});

describe('naming helpers', () => {
  it.each([
    ['RequestType', 'request-type'],
    ['requestType', 'request-type'],
    ['record', 'record'],
    ['HTTPRequest', 'http-request'],
    ['SessionUser2Fa', 'session-user2-fa'],
  ])('toKebabCase(%s) = %s', (input, expected) => {
    expect(toKebabCase(input)).toBe(expected);
  });

  it('strips one role suffix but never the whole name', () => {
    expect(stripSchemaSuffix('SessionUserResponse')).toBe('SessionUser');
    expect(stripSchemaSuffix('RecordSummary')).toBe('Record');
    expect(stripSchemaSuffix('Response')).toBe('Response');
    expect(stripSchemaSuffix('Record')).toBe('Record');
  });

  it('decodes JSON pointer escapes', () => {
    expect(pointerSegments('#/paths/~1api~1records/get/x~0y')).toEqual(['paths', '/api/records', 'get', 'x~y']);
    expect(pointerSegments(undefined)).toEqual([]);
  });
});

describe('wireIdDomain', () => {
  it.each([
    ['#/components/schemas/RecordResponse/id', 'record'],
    ['#/components/schemas/SessionUser/id', 'session-user'],
    ['#/components/schemas/RecordResponse/requestTypeId', 'request-type'],
    ['#/components/schemas/RecordResponse/attachmentIds', 'attachment'],
    ['#/components/schemas/RecordResponse/properties/ownerId', 'owner'],
    ['#/components/schemas/RecordResponse/lines/items/productId', 'product'],
    ['#/components/schemas/RecordId', 'record'],
    ['#/paths/~1api~1records~1{recordId}/get/parameters/path/recordId', 'record'],
    ['#/paths/~1api~1records~1{id}/get/parameters/path/id', null],
    ['#/components/schemas/RecordResponse/reference', null],
    ['#/webhooks/x', null],
    [undefined, null],
  ])('%s -> %s', (pointer, domain) => {
    expect(wireIdDomain(publicId(), pointer)).toBe(domain);
  });

  it('prefers the x-codelab-id-domain extension', () => {
    expect(wireIdDomain(publicId({ 'x-codelab-id-domain': ' employee ' }), '#/components/schemas/A/approverRef')).toBe('employee');
  });

  it('ignores a blank extension', () => {
    expect(wireIdDomain(publicId({ 'x-codelab-id-domain': ' ' }), '#/components/schemas/A/ownerId')).toBe('owner');
  });
});

describe('createWireIdTransform', () => {
  it('returns undefined for schemas that are not PublicIds, so openapi-typescript keeps its own type', () => {
    expect(createWireIdTransform(ts)({ type: 'string' }, { path: '#/components/schemas/A/name' })).toBeUndefined();
  });

  it('emits WireId<domain>, WireId<string> and the nullable union', () => {
    const transform = createWireIdTransform(ts);
    expect(print(transform(publicId(), { path: '#/components/schemas/RecordResponse/id' }))).toBe("WireId<'record'>");
    expect(print(transform(publicId(), { path: '#/components/schemas/RecordResponse/reference' }))).toBe('WireId<string>');
    expect(print(transform(publicId({ type: ['string', 'null'] }), { path: '#/components/schemas/RecordResponse/ownerId' })))
      .toBe("WireId<'owner'> | null");
  });

  it('keeps the domain of the first (property) call when openapi-typescript calls again with the parent path', () => {
    const transform = createWireIdTransform(ts);
    const schema = publicId();
    transform(schema, { path: '#/components/schemas/RecordResponse/requestTypeId' });
    expect(print(transform(schema, { path: '#/components/schemas/RecordResponse' }))).toBe("WireId<'request-type'>");
  });

  it('injects a type-only import of WireId from @codelab/ng-security', () => {
    expect(WIRE_ID_IMPORT_LINE).toBe("import type { WireId } from '@codelab/ng-security';");
  });
});
