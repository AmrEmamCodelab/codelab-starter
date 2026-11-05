import { parseAppConfig, RUNTIME_CONFIG_PATH, type AppConfig } from './app-config';
import { fetchStaticJson, type StaticJsonFetch } from '../http/static-json.loader';

/**
 * Loads and validates `config.json` before bootstrap (`main.ts`). Fails loudly: an app that silently
 * talks to the wrong API is worse than one that refuses to start.
 *
 * @throws StaticFileLoadError when the file does not arrive; AppConfigInvalidError when it is malformed.
 */
export async function loadRuntimeConfig(fetchJson: StaticJsonFetch = fetchStaticJson): Promise<AppConfig> {
  return parseAppConfig(await fetchJson(RUNTIME_CONFIG_PATH));
}
