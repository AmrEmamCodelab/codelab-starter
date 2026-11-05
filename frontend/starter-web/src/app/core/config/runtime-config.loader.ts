import { isDevMode } from '@angular/core';
import { parseAppConfig, RUNTIME_CONFIG_PATH, type AppConfig } from './app-config';
import { fetchStaticJson, type StaticJsonFetch } from '../http/static-json.loader';

/**
 * What a development build (`npm start`: ng serve on :4280, `run-local.ps1`, the visual harness) forces over
 * `config.json`. The shipped `config.json` keeps `"showcase": false` (tools/shipped-config.test.mjs), so a production
 * build never exposes the anonymous `/showcase` routes; a development build turns them on without a config edit.
 * Staging runs the production build and turns the showcase on in its own deployed `config.json`.
 */
export const DEVELOPMENT_BUILD_OVERRIDES: Readonly<Pick<AppConfig, 'showcase'>> = Object.freeze({ showcase: true });

/** The parsed config, with {@link DEVELOPMENT_BUILD_OVERRIDES} applied only in a development build. */
export function applyBuildOverrides(config: AppConfig, developmentBuild: boolean): AppConfig {
  return developmentBuild ? { ...config, ...DEVELOPMENT_BUILD_OVERRIDES } : config;
}

/**
 * Loads and validates `config.json` before bootstrap (`main.ts`). Fails loudly: an app that silently
 * talks to the wrong API is worse than one that refuses to start.
 *
 * @param developmentBuild true in a development build (Angular's `isDevMode()`, false in every optimized build).
 * @throws StaticFileLoadError when the file does not arrive; AppConfigInvalidError when it is malformed.
 */
export async function loadRuntimeConfig(
  fetchJson: StaticJsonFetch = fetchStaticJson,
  developmentBuild: boolean = isDevMode(),
): Promise<AppConfig> {
  return applyBuildOverrides(parseAppConfig(await fetchJson(RUNTIME_CONFIG_PATH)), developmentBuild);
}
