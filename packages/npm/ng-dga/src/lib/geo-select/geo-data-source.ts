import type { DgaSelectOption } from '../select/select-options';

/**
 * The contract between `dga-geo-select` and the app that feeds it. The component is presentational and never makes an
 * HTTP call: the app implements {@link DgaGeoDataSource} (static seed data, or its API service) and passes it in.
 */

/** The id of a region, city or district, as the app's data uses it. */
export type DgaGeoId = string | number;

/** A place name in both languages; the component shows the one for its `language`. */
export interface DgaGeoName {
  readonly ar: string;
  readonly en: string;
}

/** A region, city or district. */
export interface DgaGeoPlace {
  readonly id: DgaGeoId;
  readonly name: DgaGeoName;
}

/** The structural shape of an RxJS observable, so the package needs no RxJS dependency. */
export interface DgaSubscribable<T> {
  subscribe(observer: { next: (value: T) => void; error: (failure: unknown) => void }): { unsubscribe(): void };
}

/** What a data-source method returns: a promise or an observable of the places. */
export type DgaGeoResult<T> = PromiseLike<T> | DgaSubscribable<T>;

/** The source of the three cascading lists. */
export interface DgaGeoDataSource {
  regions(): DgaGeoResult<readonly DgaGeoPlace[]>;
  cities(regionId: DgaGeoId): DgaGeoResult<readonly DgaGeoPlace[]>;
  districts(cityId: DgaGeoId): DgaGeoResult<readonly DgaGeoPlace[]>;
}

/** The chosen place at each level; null where nothing is chosen. */
export interface DgaGeoSelection {
  readonly regionId: DgaGeoId | null;
  readonly cityId: DgaGeoId | null;
  readonly districtId: DgaGeoId | null;
}

export const DGA_GEO_EMPTY_SELECTION: DgaGeoSelection = { regionId: null, cityId: null, districtId: null };

/** How many levels the field shows. */
export const DgaGeoDepth = {
  Region: 1,
  City: 2,
  District: 3,
} as const;
export type DgaGeoDepth = (typeof DgaGeoDepth)[keyof typeof DgaGeoDepth];

/** One level of the cascade, as named in a load failure. */
export const DgaGeoLevel = {
  Region: 'region',
  City: 'city',
  District: 'district',
} as const;
export type DgaGeoLevel = (typeof DgaGeoLevel)[keyof typeof DgaGeoLevel];

/** The language the place names are shown in. */
export const DgaGeoLanguage = {
  Arabic: 'ar',
  English: 'en',
} as const;
export type DgaGeoLanguage = (typeof DgaGeoLanguage)[keyof typeof DgaGeoLanguage];

/** A new selection with the region chosen; a different region clears the city and district. */
export function withGeoRegion(selection: DgaGeoSelection, regionId: DgaGeoId | null): DgaGeoSelection {
  if (Object.is(selection.regionId, regionId)) return selection;
  return { regionId, cityId: null, districtId: null };
}

/** A new selection with the city chosen; a different city clears the district. */
export function withGeoCity(selection: DgaGeoSelection, cityId: DgaGeoId | null): DgaGeoSelection {
  if (Object.is(selection.cityId, cityId)) return selection;
  return { ...selection, cityId, districtId: null };
}

/** A new selection with the district chosen. */
export function withGeoDistrict(selection: DgaGeoSelection, districtId: DgaGeoId | null): DgaGeoSelection {
  return Object.is(selection.districtId, districtId) ? selection : { ...selection, districtId };
}

/** True when every level the field shows has a choice. */
export function isGeoSelectionComplete(selection: DgaGeoSelection, depth: DgaGeoDepth): boolean {
  if (selection.regionId === null) return false;
  if (depth >= DgaGeoDepth.City && selection.cityId === null) return false;
  return depth < DgaGeoDepth.District || selection.districtId !== null;
}

/** The select options for places, labelled in the language, in the source's order. */
export function geoPlaceOptions(places: readonly DgaGeoPlace[], language: DgaGeoLanguage): DgaSelectOption<DgaGeoId>[] {
  return places.map((place) => ({ value: place.id, label: place.name[language] }));
}

function isSubscribable<T>(result: DgaGeoResult<T>): result is DgaSubscribable<T> {
  return typeof (result as Partial<DgaSubscribable<T>>).subscribe === 'function';
}

/**
 * Starts reading a data-source result and returns a cancel function. After cancel, neither callback runs: a slow
 * answer for a region the person already left can never overwrite the list of the region they chose next.
 */
export function readGeoResult<T>(result: DgaGeoResult<T>, handlers: { readonly next: (value: T) => void; readonly error: (failure: unknown) => void }): () => void {
  let cancelled = false;
  const next = (value: T) => {
    if (!cancelled) handlers.next(value);
  };
  const error = (failure: unknown) => {
    if (!cancelled) handlers.error(failure);
  };
  if (isSubscribable(result)) {
    const subscription = result.subscribe({ next, error });
    return () => {
      cancelled = true;
      subscription.unsubscribe();
    };
  }
  result.then(next, error);
  return () => {
    cancelled = true;
  };
}
