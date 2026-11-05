import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { GEO_DATA_SOURCE, StaticGeoDataSource } from './geo-data-source';
import { GEO_SEED } from './geo-seed-data';

const REGION_COUNT = 13;

describe('StaticGeoDataSource', () => {
  const source = new StaticGeoDataSource();

  it('lists the 13 regions with Arabic and English names', async () => {
    const regions = await source.regions();
    expect(regions).toHaveLength(REGION_COUNT);
    expect(regions[0]).toEqual({ id: 'riyadh', name: { ar: 'الرياض', en: 'Riyadh' } });
    for (const region of regions) {
      expect(region.name.ar.length).toBeGreaterThan(0);
      expect(region.name.en.length).toBeGreaterThan(0);
    }
  });

  it('lists the cities of a region and the districts of a city', async () => {
    expect((await source.cities('makkah')).map((city) => city.id)).toEqual(['makkah-city', 'jeddah', 'taif']);
    expect((await source.districts('riyadh-city')).map((district) => district.name.en)).toContain('Al Olaya');
  });

  it('gives empty lists for unknown ids and for cities without seeded districts', async () => {
    expect(await source.cities('nowhere')).toEqual([]);
    expect(await source.districts('nowhere')).toEqual([]);
    expect(await source.districts('taif')).toEqual([]);
  });

  it('returns places without the nested seed lists', async () => {
    const [region] = await source.cities('riyadh');
    expect(Object.keys(region).sort()).toEqual(['id', 'name']);
  });
});

describe('the seed', () => {
  it('uses ids that are unique across every level', () => {
    const ids = GEO_SEED.flatMap((region) => [region.id, ...region.cities.flatMap((city) => [city.id, ...city.districts.map((district) => district.id)])]);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('gives every region at least one city', () => {
    for (const region of GEO_SEED) expect(region.cities.length).toBeGreaterThan(0);
  });
});

describe('GEO_DATA_SOURCE', () => {
  it('defaults to the static seed source and can be replaced', () => {
    expect(TestBed.inject(GEO_DATA_SOURCE)).toBeInstanceOf(StaticGeoDataSource);
    const replacement = { regions: () => Promise.resolve([]), cities: () => Promise.resolve([]), districts: () => Promise.resolve([]) };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [{ provide: GEO_DATA_SOURCE, useValue: replacement }] });
    expect(TestBed.inject(GEO_DATA_SOURCE)).toBe(replacement);
  });
});
