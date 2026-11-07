import { fetchStaticJson, type StaticJsonFetch } from '../http/static-json.loader';
import { assertBasePath } from './base-path';
import { parseOpsConfig, RUNTIME_CONFIG_PATH, type OpsConfig } from './ops-config';

/**
 * Loads and validates config.json before bootstrap (`main.ts`) and checks it against the served base href.
 *
 * @param baseUri the document's base URL (`document.baseURI`).
 * @throws StaticFileLoadError when the file does not arrive; OpsConfigInvalidError when it is malformed;
 *   BasePathMismatchError when its `basePath` is not the document's base path.
 */
export async function loadRuntimeConfig(
  baseUri: string,
  fetchJson: StaticJsonFetch = fetchStaticJson,
): Promise<OpsConfig> {
  const config = parseOpsConfig(await fetchJson(RUNTIME_CONFIG_PATH));
  assertBasePath(config, baseUri);
  return config;
}
