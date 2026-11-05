// The WireId transform: every PublicId field of the OpenAPI document becomes `WireId<'domain'>` from
// @codelab/ng-security instead of a plain `string`, so an id from one family cannot be passed where another is due.
//
// Codelab.Security's PublicIdSchemaTransformer writes PublicIds as `type: string` with PublicIdFormat.OpenApiPattern
// and no `format`, so the pattern is the marker. The OpenAPI schema does not carry the id family, so the domain is
// derived from where the field sits (see wireIdDomain); `x-codelab-id-domain` on the schema overrides it.

import { ID_DOMAIN_EXTENSION, PUBLIC_ID_PATTERN, SCHEMA_NAME_SUFFIXES, WireIdImport } from './constants.mjs';

const STRING_TYPE = 'string';
const NULL_TYPE = 'null';
const POINTER_SEPARATOR = '/';
const SCHEMAS_SEGMENT = 'schemas';
const STRUCTURAL_SEGMENTS = new Set(['properties', 'items', 'additionalProperties', 'prefixItems', 'allOf', 'anyOf', 'oneOf']);
const PARAMETERS_SEGMENT = 'parameters';
const ID_PROPERTY = 'id';
const ID_SUFFIX = 'Id';
const IDS_SUFFIX = 'Ids';

/** True when the schema is a PublicId as Codelab.Security documents it. */
export function isPublicIdSchema(schema) {
  if (schema === null || typeof schema !== 'object' || schema.pattern !== PUBLIC_ID_PATTERN) return false;
  const types = Array.isArray(schema.type) ? schema.type : [schema.type];
  return types.includes(STRING_TYPE);
}

/** True when the PublicId may be null (OpenAPI 3.1 `type: [string, null]` or 3.0 `nullable: true`). */
export function isNullableSchema(schema) {
  return (Array.isArray(schema.type) && schema.type.includes(NULL_TYPE)) || schema.nullable === true;
}

/** `RequestType` / `requestType` -> `request-type`. */
export function toKebabCase(name) {
  return name
    .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
    .replace(/([A-Z])([A-Z][a-z])/g, '$1-$2')
    .toLowerCase();
}

/** Decodes a JSON pointer (`#/components/schemas/A~1B`) into its segments. */
export function pointerSegments(pointer) {
  return (pointer ?? '')
    .replace(/^#/, '')
    .split(POINTER_SEPARATOR)
    .filter((segment) => segment !== '')
    .map((segment) => segment.replaceAll('~1', POINTER_SEPARATOR).replaceAll('~0', '~'));
}

/** A schema name without its role suffix: `SessionUserResponse` -> `SessionUser`. */
export function stripSchemaSuffix(schemaName) {
  const suffix = SCHEMA_NAME_SUFFIXES.find((candidate) => schemaName.endsWith(candidate) && schemaName !== candidate);
  return suffix === undefined ? schemaName : schemaName.slice(0, -suffix.length);
}

/** The domain an `<x>Id` / `<x>Ids` property or parameter name implies, or null for any other name. */
function domainFromIdName(name) {
  for (const suffix of [IDS_SUFFIX, ID_SUFFIX]) {
    if (name.length > suffix.length && name.endsWith(suffix)) return toKebabCase(name.slice(0, -suffix.length));
  }
  return null;
}

/** True for pointer segments that are schema structure, not field names (`items`, `allOf`, an index...). */
function isStructuralSegment(segment) {
  return STRUCTURAL_SEGMENTS.has(segment) || /^d+$/.test(segment);
}

/**
 * The id domain of a PublicId at `pointer` (the openapi-typescript transform path), or null when none can be
 * derived (the type is then `WireId<string>`):
 * 1. the `x-codelab-id-domain` extension on the schema;
 * 2. a parameter (`#/paths/.../parameters/path/recordId`) named `<x>Id` or `<x>Ids` -> `<x>` in kebab case;
 * 3. a schema field (`#/components/schemas/<Schema>/<field>`, array items included) named `<x>Id` or `<x>Ids` ->
 *    `<x>` (`requestTypeId` -> `request-type`);
 * 4. a field named `id`, or a schema that is itself a PublicId -> the schema name without its role suffix
 *    (`SessionUserResponse.id` -> `session-user`).
 */
export function wireIdDomain(schema, pointer) {
  const explicit = schema?.[ID_DOMAIN_EXTENSION];
  if (typeof explicit === 'string' && explicit.trim() !== '') return explicit.trim();
  const segments = pointerSegments(pointer);
  if (segments.includes(PARAMETERS_SEGMENT)) return segments.length === 0 ? null : domainFromIdName(segments.at(-1));
  const schemasIndex = segments.indexOf(SCHEMAS_SEGMENT);
  if (schemasIndex < 0 || schemasIndex + 1 >= segments.length) return null;
  const schemaName = segments[schemasIndex + 1];
  const fieldNames = segments.slice(schemasIndex + 2).filter((segment) => !isStructuralSegment(segment));
  if (fieldNames.length === 0) return domainFromIdName(schemaName) ?? toKebabCase(stripSchemaSuffix(schemaName));
  const fieldName = fieldNames.at(-1);
  return fieldName === ID_PROPERTY ? toKebabCase(stripSchemaSuffix(schemaName)) : domainFromIdName(fieldName);
}

/** The `WireId<'domain'>` (or `WireId<string>`) type node, with `| null` when the schema is nullable. */
export function wireIdTypeNode(ts, domain, nullable) {
  const argument = domain === null
    ? ts.factory.createKeywordTypeNode(ts.SyntaxKind.StringKeyword)
    : ts.factory.createLiteralTypeNode(ts.factory.createStringLiteral(domain, true));
  const wireId = ts.factory.createTypeReferenceNode(WireIdImport.typeName, [argument]);
  return nullable
    ? ts.factory.createUnionTypeNode([wireId, ts.factory.createLiteralTypeNode(ts.factory.createNull())])
    : wireId;
}

/**
 * The openapi-typescript `transform` hook. `ts` is the TypeScript module whose factory builds the nodes.
 *
 * openapi-typescript 7 calls the hook twice for an object property: first with the property's own path
 * (`.../<Schema>/<field>`), then again with the SAME schema object but the parent's path (`.../<Schema>`), and the
 * second answer wins. The first, precise domain is therefore remembered per schema object.
 */
export function createWireIdTransform(ts) {
  const domains = new WeakMap();
  return (schemaObject, options) => {
    if (!isPublicIdSchema(schemaObject)) return undefined;
    if (!domains.has(schemaObject)) domains.set(schemaObject, wireIdDomain(schemaObject, options?.path));
    return wireIdTypeNode(ts, domains.get(schemaObject), isNullableSchema(schemaObject));
  };
}

/** The import line injected at the top of the generated client. */
export const WIRE_ID_IMPORT_LINE = `import type { ${WireIdImport.typeName} } from '${WireIdImport.moduleName}';`;
