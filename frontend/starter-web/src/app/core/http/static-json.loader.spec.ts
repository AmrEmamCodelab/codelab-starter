import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchStaticJson, NO_RESPONSE_STATUS, StaticFileLoadError } from './static-json.loader';

const NOT_FOUND = 404;
const OK = 200;

describe('fetchStaticJson', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('returns the parsed body without caching', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{"a":1}', { status: OK }));
    vi.stubGlobal('fetch', fetchMock);
    await expect(fetchStaticJson('/config.json')).resolves.toEqual({ a: 1 });
    expect(fetchMock).toHaveBeenCalledWith('/config.json', { cache: 'no-store' });
  });

  it('reports the HTTP status of a failed response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: NOT_FOUND })));
    await expect(fetchStaticJson('/x.json')).rejects.toMatchObject({ status: NOT_FOUND, path: '/x.json' });
  });

  it('reports a network failure as no response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));
    await expect(fetchStaticJson('/x.json')).rejects.toMatchObject({ status: NO_RESPONSE_STATUS });
  });

  it('reports a body that is not JSON', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<html>', { status: OK })));
    await expect(fetchStaticJson('/x.json')).rejects.toBeInstanceOf(StaticFileLoadError);
  });
});
