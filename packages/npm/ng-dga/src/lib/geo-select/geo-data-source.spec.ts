import { describe, expect, it, vi } from 'vitest';
import {
  DGA_GEO_EMPTY_SELECTION,
  DgaGeoDepth,
  DgaGeoLanguage,
  type DgaSubscribable,
  geoPlaceOptions,
  isGeoSelectionComplete,
  readGeoResult,
  withGeoCity,
  withGeoDistrict,
  withGeoRegion,
} from './geo-data-source';

const FULL = { regionId: 'r1', cityId: 'c1', districtId: 'd1' };

describe('the cascade reset', () => {
  it('clears city and district when the region changes', () => {
    expect(withGeoRegion(FULL, 'r2')).toEqual({ regionId: 'r2', cityId: null, districtId: null });
    expect(withGeoRegion(FULL, null)).toEqual(DGA_GEO_EMPTY_SELECTION);
  });

  it('clears only the district when the city changes', () => {
    expect(withGeoCity(FULL, 'c2')).toEqual({ regionId: 'r1', cityId: 'c2', districtId: null });
  });

  it('sets the district alone', () => {
    expect(withGeoDistrict(FULL, 'd2')).toEqual({ ...FULL, districtId: 'd2' });
  });

  it('returns the same selection when nothing changes', () => {
    expect(withGeoRegion(FULL, 'r1')).toBe(FULL);
    expect(withGeoCity(FULL, 'c1')).toBe(FULL);
    expect(withGeoDistrict(FULL, 'd1')).toBe(FULL);
  });
});

describe('isGeoSelectionComplete', () => {
  it('needs a choice at every shown level', () => {
    const regionOnly = { regionId: 1, cityId: null, districtId: null };
    expect(isGeoSelectionComplete(regionOnly, DgaGeoDepth.Region)).toBe(true);
    expect(isGeoSelectionComplete(regionOnly, DgaGeoDepth.City)).toBe(false);
    expect(isGeoSelectionComplete({ ...regionOnly, cityId: 2 }, DgaGeoDepth.City)).toBe(true);
    expect(isGeoSelectionComplete({ ...regionOnly, cityId: 2 }, DgaGeoDepth.District)).toBe(false);
    expect(isGeoSelectionComplete(FULL, DgaGeoDepth.District)).toBe(true);
    expect(isGeoSelectionComplete(DGA_GEO_EMPTY_SELECTION, DgaGeoDepth.Region)).toBe(false);
  });
});

describe('geoPlaceOptions', () => {
  it('labels the places in the requested language, keeping their order', () => {
    const places = [
      { id: 'b', name: { ar: 'ب', en: 'B' } },
      { id: 'a', name: { ar: 'أ', en: 'A' } },
    ];
    expect(geoPlaceOptions(places, DgaGeoLanguage.Arabic)).toEqual([
      { value: 'b', label: 'ب' },
      { value: 'a', label: 'أ' },
    ]);
    expect(geoPlaceOptions(places, DgaGeoLanguage.English).map((option) => option.label)).toEqual(['B', 'A']);
  });
});

describe('readGeoResult', () => {
  it('delivers a promise result, or its failure', async () => {
    const next = vi.fn();
    const error = vi.fn();
    readGeoResult(Promise.resolve([1]), { next, error });
    readGeoResult(Promise.reject(new Error('down')), { next, error });
    await Promise.resolve();
    await Promise.resolve();
    expect(next).toHaveBeenCalledWith([1]);
    expect(error).toHaveBeenCalledWith(new Error('down'));
  });

  it('drops a promise result that arrives after cancel', async () => {
    const next = vi.fn();
    const cancel = readGeoResult(Promise.resolve([1]), { next, error: vi.fn() });
    cancel();
    await Promise.resolve();
    expect(next).not.toHaveBeenCalled();
  });

  it('subscribes to an observable and unsubscribes on cancel', () => {
    const unsubscribe = vi.fn();
    let push: (value: number[]) => void = () => undefined;
    const source: DgaSubscribable<number[]> = {
      subscribe(observer) {
        push = observer.next;
        return { unsubscribe };
      },
    };
    const next = vi.fn();
    const cancel = readGeoResult(source, { next, error: vi.fn() });
    push([7]);
    cancel();
    push([8]);
    expect(next).toHaveBeenCalledTimes(1);
    expect(next).toHaveBeenCalledWith([7]);
    expect(unsubscribe).toHaveBeenCalledTimes(1);
  });
});
