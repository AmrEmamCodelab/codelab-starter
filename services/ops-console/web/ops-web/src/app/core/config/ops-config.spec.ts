import { describe, expect, it, vi } from 'vitest';
import { StaticFileLoadError } from '../http/static-json.loader';
import { BasePathMismatchError, assertBasePath, documentBasePath } from './base-path';
import { OpsConfigInvalidError, OpsConfigKeys, RUNTIME_CONFIG_PATH, parseOpsConfig } from './ops-config';
import { loadRuntimeConfig } from './runtime-config.loader';

const VALID = { basePath: '/ops/', apiBaseUrl: '/ops-api', defaultLanguage: 'ar', idpCertificateName: null };

function keyOf(action: () => unknown): string | null {
  try {
    action();
    return null;
  } catch (error) {
    return error instanceof OpsConfigInvalidError ? error.key : 'other';
  }
}

describe('parseOpsConfig', () => {
  it('accepts the shipped shape', () => {
    expect(parseOpsConfig(VALID)).toEqual(VALID);
  });

  it('trims trailing slashes from apiBaseUrl', () => {
    expect(parseOpsConfig({ ...VALID, apiBaseUrl: '/ops-api//' }).apiBaseUrl).toBe('/ops-api');
  });

  it('keeps a configured IdP certificate name and treats a missing one as null', () => {
    expect(parseOpsConfig({ ...VALID, idpCertificateName: 'idp-signing' }).idpCertificateName).toBe('idp-signing');
    const { idpCertificateName: _ignored, ...withoutName } = VALID;
    expect(parseOpsConfig(withoutName).idpCertificateName).toBeNull();
  });

  it.each([null, [], 'text', 3])('refuses a config that is not an object (%s)', (raw) => {
    expect(keyOf(() => parseOpsConfig(raw))).toBe(RUNTIME_CONFIG_PATH);
  });

  it.each(['ops/', '/ops', '//evil/', '/a/../', '\\ops\\', '', 7])('refuses basePath %s', (basePath) => {
    expect(keyOf(() => parseOpsConfig({ ...VALID, basePath }))).toBe(OpsConfigKeys.BasePath);
  });

  it.each(['https://other.example/ops-api', '//other.example/ops-api', 'ops-api', '/', '/x/../y', null])(
    'refuses apiBaseUrl %s (never another origin)',
    (apiBaseUrl) => {
      expect(keyOf(() => parseOpsConfig({ ...VALID, apiBaseUrl }))).toBe(OpsConfigKeys.ApiBaseUrl);
    },
  );

  it.each(['fr', '', null])('refuses defaultLanguage %s', (defaultLanguage) => {
    expect(keyOf(() => parseOpsConfig({ ...VALID, defaultLanguage }))).toBe(OpsConfigKeys.DefaultLanguage);
  });

  it.each(['', '   ', 5])('refuses idpCertificateName %s', (idpCertificateName) => {
    expect(keyOf(() => parseOpsConfig({ ...VALID, idpCertificateName }))).toBe(OpsConfigKeys.IdpCertificateName);
  });
});

describe('base path', () => {
  it('reads the path of the document base URL', () => {
    expect(documentBasePath('https://portal.example/ops/')).toBe('/ops/');
  });

  it('passes when the served base href matches', () => {
    expect(() => assertBasePath({ basePath: '/ops/' }, 'https://portal.example/ops/')).not.toThrow();
  });

  it('refuses a mismatch with both values in the error', () => {
    try {
      assertBasePath({ basePath: '/ops/' }, 'https://portal.example/');
      expect.unreachable();
    } catch (error) {
      expect(error).toBeInstanceOf(BasePathMismatchError);
      expect((error as BasePathMismatchError).configured).toBe('/ops/');
      expect((error as BasePathMismatchError).served).toBe('/');
    }
  });
});

describe('loadRuntimeConfig', () => {
  it('fetches config.json relative to the base and validates it', async () => {
    const fetchJson = vi.fn(async () => VALID);
    await expect(loadRuntimeConfig('https://portal.example/ops/', fetchJson)).resolves.toEqual(VALID);
    expect(fetchJson).toHaveBeenCalledWith(RUNTIME_CONFIG_PATH);
  });

  it('fails when the base href does not match', async () => {
    await expect(loadRuntimeConfig('https://portal.example/other/', async () => VALID)).rejects.toBeInstanceOf(BasePathMismatchError);
  });

  it('passes a load failure through', async () => {
    const failure = new StaticFileLoadError(RUNTIME_CONFIG_PATH, 404);
    await expect(loadRuntimeConfig('https://portal.example/ops/', async () => { throw failure; })).rejects.toBe(failure);
  });
});
