import { describe, expect, it } from 'vitest';
import { createApiRootClassifier } from './service-url.ts';

const PAGE = 'https://portal.example.test';

function classifier(...roots: string[]) {
  return createApiRootClassifier({ roots: () => roots, pageOrigin: () => PAGE });
}

describe('createApiRootClassifier', () => {
  it('strips a relative root, so an edge-proxied call signs the path the API sees', () => {
    const classify = classifier('/api');

    expect(classify('/api/items/1?x=1')).toBe('/items/1');
    expect(classify(`${PAGE}/api/items`)).toBe('/items');
  });

  it('gives one path for the relative and the split-host spellings of one call', () => {
    expect(classifier('/api')('/api/auth/me')).toBe(classifier('https://api.example.test')('https://api.example.test/auth/me'));
  });

  it('signs a single slash for the bare root', () => {
    expect(classifier('/api')('/api')).toBe('/');
    expect(classifier('/api/')('/api/')).toBe('/');
  });

  it('matches whole path segments only', () => {
    expect(classifier('/api')('/apix/items')).toBeNull();
    expect(classifier('/api')('/static/app.js')).toBeNull();
  });

  it('keeps the whole path when the API is served at an origin root', () => {
    expect(classifier('https://api.example.test/')('https://api.example.test/items/2')).toBe('/items/2');
  });

  it('does not sign another origin, including the page itself, for a split-host API', () => {
    const classify = classifier('https://api.example.test');

    expect(classify('https://cdn.example.test/items')).toBeNull();
    expect(classify('/items')).toBeNull();
    expect(classify('http://api.example.test/items')).toBeNull();
  });

  it('does not sign a userinfo spelling of the configured origin', () => {
    expect(classifier('https://api.example.test')('https://api.example.test@evil.example/items')).toBeNull();
    expect(classifier('https://api.example.test')('https://user@api.example.test/items')).toBeNull();
  });

  it('returns the percent-encoded path the browser sends', () => {
    expect(classifier('/api')('/api/items/طلب')).toBe('/items/%D8%B7%D9%84%D8%A8');
  });

  it('tries every root and reads them lazily', () => {
    let roots: string[] = [];
    const classify = createApiRootClassifier({ roots: () => roots, pageOrigin: () => PAGE });
    expect(classify('/files/1')).toBeNull();

    roots = ['/api', '/files'];
    expect(classify('/files/1')).toBe('/1');
  });

  it('ignores a root it cannot parse instead of throwing', () => {
    expect(classifier('http://[bad', '/api')('/api/x')).toBe('/x');
  });
});
