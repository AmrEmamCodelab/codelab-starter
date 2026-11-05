// openapi.v1.json -> src/app/core/api/generated/openapi.ts with openapi-typescript and the WireId transform.

import { generatedHeader } from './constants.mjs';
import { WIRE_ID_IMPORT_LINE, createWireIdTransform } from './wire-id.mjs';

const OPENAPI_SOURCE = 'docs/generated/openapi.v1.json';
const OPENAPI_TYPESCRIPT_PACKAGE = 'openapi-typescript';
const TYPESCRIPT_PACKAGE = 'typescript';

/**
 * Loads openapi-typescript and TypeScript. They are frontend devDependencies, so the error names the install step
 * instead of a bare ERR_MODULE_NOT_FOUND.
 */
export async function loadGeneratorModules() {
  try {
    const [openapi, typescript] = await Promise.all([import(OPENAPI_TYPESCRIPT_PACKAGE), import(TYPESCRIPT_PACKAGE)]);
    return { openapi, ts: typescript.default ?? typescript };
  } catch (error) {
    throw new Error(
      `${OPENAPI_TYPESCRIPT_PACKAGE} and ${TYPESCRIPT_PACKAGE} must be installed (npm install at the repository root): ${error.message}`,
      { cause: error },
    );
  }
}

/**
 * The full text of the generated client. `modules` is `{ openapi, ts }` from loadGeneratorModules (tests inject
 * their own); `document` is the parsed OpenAPI document.
 */
export async function renderApiClient(document, modules) {
  const { openapi, ts } = modules;
  const nodes = await openapi.default(document, {
    transform: createWireIdTransform(ts),
    inject: WIRE_ID_IMPORT_LINE,
    alphabetize: true,
    silent: true,
  });
  const body = openapi.astToString(nodes).replace(/\r\n/g, '\n');
  return `${generatedHeader(OPENAPI_SOURCE)}\n${body.endsWith('\n') ? body : `${body}\n`}`;
}
