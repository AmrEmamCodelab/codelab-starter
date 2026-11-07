import { afterEach, describe, expect, it, vi } from 'vitest';
import { NO_RESPONSE_STATUS, StaticFileLoadError, fetchStaticJson } from './static-json.loader';

function stubFetch(implementation: () => Promise<unknown>): void {
  vi.stubGlobal('fetch', vi.fn(implementation));
}

describe('fetchStaticJson', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('returns the parsed body and never caches', async () => {
    stubFetch(async () => ({ ok: true, status: 200, json: async () => ({ a: 1 }) }));
    await expect(fetchStaticJson('config.json')).resolves.toEqual({ a: 1 });
    expect(fetch).toHaveBeenCalledWith('config.json', { cache: 'no-store', credentials: 'same-origin' });
  });

  it('reports a network failure as status 0', async () => {
    stubFetch(async () => {
      throw new TypeError('offline');
    });
    await expect(fetchStaticJson('config.json')).rejects.toMatchObject({ status: NO_RESPONSE_STATUS, path: 'config.json' });
  });

  it('reports an HTTP failure with its status', async () => {
    stubFetch(async () => ({ ok: false, status: 404, json: async () => ({}) }));
    await expect(fetchStaticJson('config.json')).rejects.toMatchObject({ status: 404 });
  });

  it('reports a body that is not JSON', async () => {
    stubFetch(async () => ({ ok: true, status: 200, json: async () => { throw new SyntaxError('bad'); } }));
    await expect(fetchStaticJson('config.json')).rejects.toBeInstanceOf(StaticFileLoadError);
  });
});
