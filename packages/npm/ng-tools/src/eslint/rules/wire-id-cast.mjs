// codelab/wire-id-cast: forbids casting to WireId<...> (or an alias of it) outside the decoder.
//
// A WireId<D> is a branded string that only @codelab/ng-security's tryAsId / asId / toIdSet may construct, because
// they check the token shape. `value as WireId<'record'>`, `<WireId<'record'>>value` and
// `value as unknown as RecordId` all skip that check and let a number, a request number or a token of another
// family travel as an identifier. The rule is syntactic: it sees WireId itself, aliases declared in the same file,
// and the alias names listed in the `aliases` option (for aliases imported from elsewhere).

const DEFAULT_BRANDED_TYPE = 'WireId';
const DEFAULT_DECODER_FILES = Object.freeze(['wire-id.ts']);
const PATH_SEPARATOR = '/';

const messages = Object.freeze({
  cast: "Do not cast to {{name}}: build wire ids with tryAsId / asId / toIdSet from '@codelab/ng-security', which check the token shape.",
});

const schema = [
  {
    type: 'object',
    properties: {
      brandedType: { type: 'string', minLength: 1 },
      aliases: { type: 'array', items: { type: 'string', minLength: 1 }, uniqueItems: true },
      decoderFiles: { type: 'array', items: { type: 'string', minLength: 1 }, uniqueItems: true },
    },
    additionalProperties: false,
  },
];

/** True when `filename` ends with one of the decoder file names (compared on forward-slash path segments). */
export function isDecoderFile(filename, decoderFiles) {
  const normalized = PATH_SEPARATOR + String(filename).replaceAll('\\', PATH_SEPARATOR);
  return decoderFiles.some((suffix) => normalized.endsWith(PATH_SEPARATOR + suffix.replace(/^\/+/, '')));
}

/** The identifier a type reference names (`WireId`, or `ns.WireId` as `WireId`), or null. */
function referencedName(typeNode) {
  if (typeNode?.type !== 'TSTypeReference') return null;
  const name = typeNode.typeName;
  if (name.type === 'Identifier') return name.name;
  if (name.type === 'TSQualifiedName') return name.right.name;
  return null;
}

/** Local `type X = WireId<...>` (or an alias of an alias) declarations, collected before the casts are checked. */
function collectLocalAliases(program, branded) {
  const aliases = new Set();
  let grew = true;
  while (grew) {
    grew = false;
    for (const statement of program.body) {
      const declaration = statement.type === 'ExportNamedDeclaration' ? statement.declaration : statement;
      if (declaration?.type !== 'TSTypeAliasDeclaration' || aliases.has(declaration.id.name)) continue;
      const target = referencedName(declaration.typeAnnotation);
      if (target !== null && (branded.has(target) || aliases.has(target))) {
        aliases.add(declaration.id.name);
        grew = true;
      }
    }
  }
  return aliases;
}

/** @type {import('eslint').Rule.RuleModule} */
export const wireIdCastRule = {
  meta: {
    type: 'problem',
    docs: { description: 'Forbid casting to WireId<...> outside the wire-id decoder.' },
    schema,
    messages,
  },
  create(context) {
    const options = context.options[0] ?? {};
    if (isDecoderFile(context.filename, options.decoderFiles ?? DEFAULT_DECODER_FILES)) return {};
    const branded = new Set([options.brandedType ?? DEFAULT_BRANDED_TYPE, ...(options.aliases ?? [])]);
    let forbidden = branded;
    const check = (node) => {
      const name = referencedName(node.typeAnnotation);
      if (name !== null && forbidden.has(name)) context.report({ node, messageId: 'cast', data: { name } });
    };
    return {
      Program(program) {
        forbidden = new Set([...branded, ...collectLocalAliases(program, branded)]);
      },
      TSAsExpression: check,
      TSTypeAssertion: check,
    };
  },
};
