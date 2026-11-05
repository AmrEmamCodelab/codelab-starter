import { Injectable, InjectionToken, inject } from '@angular/core';
import type { DgaGeoDataSource, DgaGeoId, DgaGeoPlace } from '@codelab/ng-dga';
import { GEO_SEED, type GeoSeedCity, type GeoSeedDistrict } from './geo-seed-data';

const NO_PLACES: readonly DgaGeoPlace[] = [];

function toPlace(entry: GeoSeedDistrict): DgaGeoPlace {
  return { id: entry.id, name: entry.name };
}

/**
 * The app's `DgaGeoDataSource` for `dga-geo-select`, over the static seed in `geo-seed-data.ts`. Unknown ids give an
 * empty list (a region with no seeded cities shows an empty city list, never an error). Phase 5 swaps in a source
 * backed by the lookups API through core `ApiService`, behind the same {@link GEO_DATA_SOURCE} token.
 */
@Injectable({ providedIn: 'root' })
export class StaticGeoDataSource implements DgaGeoDataSource {
  regions(): Promise<readonly DgaGeoPlace[]> {
    return Promise.resolve(GEO_SEED.map(toPlace));
  }

  cities(regionId: DgaGeoId): Promise<readonly DgaGeoPlace[]> {
    const region = GEO_SEED.find((entry) => entry.id === regionId);
    return Promise.resolve(region === undefined ? NO_PLACES : region.cities.map(toPlace));
  }

  districts(cityId: DgaGeoId): Promise<readonly DgaGeoPlace[]> {
    const city = GEO_SEED.flatMap((region) => region.cities).find((entry: GeoSeedCity) => entry.id === cityId);
    return Promise.resolve(city === undefined ? NO_PLACES : city.districts.map(toPlace));
  }
}

/**
 * The geo data source a feature hands to `dga-geo-select`: `readonly geoData = inject(GEO_DATA_SOURCE);`. Defaults to
 * the static seed; a test or a later phase provides another implementation.
 */
export const GEO_DATA_SOURCE = new InjectionToken<DgaGeoDataSource>('GEO_DATA_SOURCE', {
  providedIn: 'root',
  factory: () => inject(StaticGeoDataSource),
});
