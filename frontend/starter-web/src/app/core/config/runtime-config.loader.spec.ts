// Regression: the shipped config.json turns the showcase off (it must, for production), and nothing turned it back on
// for development, so `npm start` and the visual harness on :4280 captured the 404 page for every /showcase route.
import { describe, expect, it } from 'vitest';
import { RUNTIME_CONFIG_PATH } from './app-config';
import { TEST_APP_CONFIG } from './app-config.testing';
import { applyBuildOverrides, DEVELOPMENT_BUILD_OVERRIDES, loadRuntimeConfig } from './runtime-config.loader';

const SHIPPED_SHAPE = { ...TEST_APP_CONFIG, showcase: false };

describe('applyBuildOverrides', () => {
  it('turns the showcase on in a development build', () => {
    expect(applyBuildOverrides(SHIPPED_SHAPE, true)).toEqual({ ...SHIPPED_SHAPE, showcase: true });
  });

  it('leaves a production build exactly as config.json says', () => {
    expect(applyBuildOverrides(SHIPPED_SHAPE, false)).toBe(SHIPPED_SHAPE);
    const staging = { ...SHIPPED_SHAPE, showcase: true };
    expect(applyBuildOverrides(staging, false).showcase).toBe(true);
  });

  it('overrides only the showcase switch', () => {
    expect(Object.keys(DEVELOPMENT_BUILD_OVERRIDES)).toEqual(['showcase']);
  });
});

describe('loadRuntimeConfig', () => {
  const fetchShipped = async (path: string): Promise<unknown> => {
    expect(path).toBe(RUNTIME_CONFIG_PATH);
    return SHIPPED_SHAPE;
  };

  it('serves the showcase in a development build even though config.json turns it off', async () => {
    expect((await loadRuntimeConfig(fetchShipped, true)).showcase).toBe(true);
  });

  it('keeps the showcase off in a production build', async () => {
    expect((await loadRuntimeConfig(fetchShipped, false)).showcase).toBe(false);
  });
});
