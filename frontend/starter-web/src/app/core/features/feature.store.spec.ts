import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { FeaturesClient } from '../api/clients/features.client';
import { FeatureFlagNames, FeatureStore } from './feature.store';

function setUp(flags: ReturnType<typeof vi.fn>): FeatureStore {
  TestBed.configureTestingModule({ providers: [{ provide: FeaturesClient, useValue: { flags } }] });
  return TestBed.inject(FeatureStore);
}

describe('FeatureStore', () => {
  it('starts with every flag off', () => {
    const store = setUp(vi.fn());
    expect(store.isEnabled(FeatureFlagNames.ProtectedTranslations)).toBe(false);
    expect(store.loaded()).toBe(false);
  });

  it('loads the flags the server sends', async () => {
    const store = setUp(vi.fn().mockReturnValue(of({ flags: { protectedTranslations: true, other: false } })));
    await store.load();
    expect(store.isEnabled(FeatureFlagNames.ProtectedTranslations)).toBe(true);
    expect(store.isEnabled('other')).toBe(false);
    expect(store.isEnabled('unknown')).toBe(false);
    expect(store.flags()).toEqual({ protectedTranslations: true, other: false });
    expect(store.loaded()).toBe(true);
  });

  it('leaves every flag off when the load fails', async () => {
    const store = setUp(vi.fn().mockReturnValue(throwError(() => new Error('offline'))));
    await store.load();
    expect(store.flags()).toEqual({});
    expect(store.loaded()).toBe(true);
  });
});
