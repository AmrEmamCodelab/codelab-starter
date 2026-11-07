import type { OpsConfig } from './ops-config';

/**
 * The served `index.html` carries a different `<base href>` than config.json's `basePath`: the deployment changed
 * `basePath` without re-applying it to index.html (`tools/apply-base-href.mjs` or the installer). Start-up stops with
 * this error rather than serving broken deep links.
 */
export class BasePathMismatchError extends Error {
  constructor(
    readonly configured: string,
    readonly served: string,
  ) {
    super(`config.json basePath "${configured}" does not match the served <base href> "${served}". Re-apply the base href.`);
    this.name = 'BasePathMismatchError';
  }
}

/** The path of the document's base URL (`https://host/ops/` gives `/ops/`). */
export function documentBasePath(baseUri: string): string {
  return new URL(baseUri).pathname;
}

/** @throws BasePathMismatchError when the path of `baseUri` differs from `config.basePath`. */
export function assertBasePath(config: Pick<OpsConfig, 'basePath'>, baseUri: string): void {
  const served = documentBasePath(baseUri);
  if (served !== config.basePath) {
    throw new BasePathMismatchError(config.basePath, served);
  }
}
