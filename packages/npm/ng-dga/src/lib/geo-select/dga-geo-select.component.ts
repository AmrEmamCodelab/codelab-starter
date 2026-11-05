import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  model,
  output,
  signal,
  untracked,
  type WritableSignal,
} from '@angular/core';
import { hasText } from '../internal/aria';
import { DgaSelectComponent } from '../select/dga-select.component';
import {
  DGA_GEO_EMPTY_SELECTION,
  type DgaGeoDataSource,
  DgaGeoDepth,
  type DgaGeoId,
  DgaGeoLanguage,
  DgaGeoLevel,
  type DgaGeoPlace,
  type DgaGeoResult,
  type DgaGeoSelection,
  geoPlaceOptions,
  readGeoResult,
  withGeoCity,
  withGeoDistrict,
  withGeoRegion,
} from './geo-data-source';

/** What one level of the cascade shows. */
interface GeoLevelState {
  readonly places: readonly DgaGeoPlace[];
  readonly loading: boolean;
  readonly failed: boolean;
}

const IDLE: GeoLevelState = { places: [], loading: false, failed: false };
const LOADING: GeoLevelState = { places: [], loading: true, failed: false };
const FAILED: GeoLevelState = { places: [], loading: false, failed: true };

/**
 * The DGA geo select: region, city and district as three cascading `dga-select`s. Presentational: the places come
 * from a {@link DgaGeoDataSource} the app passes in (static data, or its API service), never from HTTP here.
 *
 * - The value is a signal model of {@link DgaGeoSelection} `{ regionId, cityId, districtId }`: bind [(value)] or a
 *   signal-forms field (value, touched, disabled, required). Choosing another region clears the city and district;
 *   another city clears the district. `isGeoSelectionComplete(value, depth)` tells a validator whether it is filled.
 * - `depth` shows 1 (region), 2 (and city) or 3 (and district) levels. A level is disabled until its parent is chosen
 *   and while it loads (the group is `aria-busy`).
 * - Names show in `language` (the app language). All texts arrive translated: the level labels, `placeholder` (the
 *   empty choice), `error` (shown on the first level still missing a choice), and `loadErrorText` (shown on a level
 *   whose list failed to load; `loadFailed` also emits that level).
 * - A slow answer for a region left behind is cancelled, so it never replaces the list of the region chosen next.
 *
 * @example
 * <dga-geo-select [dataSource]="geoData" [language]="language()" [regionLabel]="'address.region' | translate"
 *   [cityLabel]="'address.city' | translate" [districtLabel]="'address.district' | translate"
 *   [placeholder]="'common.choose' | translate" [loadErrorText]="'common.loadFailed' | translate" required [(value)]="place" />
 */
@Component({
  selector: 'dga-geo-select',
  imports: [DgaSelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-geo-select.component.html',
  styleUrl: './dga-geo-select.component.css',
})
export class DgaGeoSelectComponent {
  readonly value = model<DgaGeoSelection>(DGA_GEO_EMPTY_SELECTION);
  readonly touched = model(false);
  /** Where the places come from. */
  readonly dataSource = input.required<DgaGeoDataSource>();
  /** The language of the place names. */
  readonly language = input<DgaGeoLanguage>(DgaGeoLanguage.Arabic);
  /** How many levels to show. */
  readonly depth = input<DgaGeoDepth>(DgaGeoDepth.District);
  /** Visible group label (already translated). */
  readonly label = input<string | null>(null);
  /** Accessible group name when there is no visible label (already translated). */
  readonly ariaLabel = input<string | null>(null);
  readonly regionLabel = input<string | null>(null);
  readonly cityLabel = input<string | null>(null);
  readonly districtLabel = input<string | null>(null);
  /** The empty choice of every level (already translated). */
  readonly placeholder = input<string | null>(null);
  /** The error message (already translated), shown on the first level still missing a choice. */
  readonly error = input<string | null>(null);
  /** Shown on a level whose list failed to load (already translated). */
  readonly loadErrorText = input<string | null>(null);
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });

  /** Emits the level whose list failed to load. */
  readonly loadFailed = output<DgaGeoLevel>();

  readonly level = DgaGeoLevel;
  readonly regions = signal(IDLE);
  readonly cities = signal(IDLE);
  readonly districts = signal(IDLE);

  readonly showCity = computed(() => this.depth() >= DgaGeoDepth.City);
  readonly showDistrict = computed(() => this.depth() >= DgaGeoDepth.District);
  readonly regionOptions = computed(() => geoPlaceOptions(this.regions().places, this.language()));
  readonly cityOptions = computed(() => geoPlaceOptions(this.cities().places, this.language()));
  readonly districtOptions = computed(() => geoPlaceOptions(this.districts().places, this.language()));
  readonly busy = computed(() => this.regions().loading || this.cities().loading || this.districts().loading);
  readonly regionDisabled = computed(() => this.disabled() || this.regions().loading);
  readonly cityDisabled = computed(() => this.disabled() || this.regionId() === null || this.cities().loading);
  readonly districtDisabled = computed(() => this.disabled() || this.cityId() === null || this.districts().loading);

  /** The level the `error` text shows on: the first shown level without a choice, else the last shown level. */
  private readonly errorLevel = computed<DgaGeoLevel | null>(() => {
    if (!hasText(this.error())) return null;
    const selection = this.value();
    if (selection.regionId === null || !this.showCity()) return DgaGeoLevel.Region;
    if (selection.cityId === null || !this.showDistrict()) return DgaGeoLevel.City;
    return DgaGeoLevel.District;
  });

  private readonly regionId = computed(() => this.value().regionId);
  private readonly cityId = computed(() => this.value().cityId);

  constructor() {
    effect((onCleanup) => {
      const source = this.dataSource();
      onCleanup(this.load({ level: DgaGeoLevel.Region, state: this.regions, read: () => source.regions() }));
    });
    effect((onCleanup) => {
      const source = this.dataSource();
      const regionId = this.regionId();
      if (regionId === null || !this.showCity()) {
        this.cities.set(IDLE);
        return;
      }
      onCleanup(this.load({ level: DgaGeoLevel.City, state: this.cities, read: () => source.cities(regionId) }));
    });
    effect((onCleanup) => {
      const source = this.dataSource();
      const cityId = this.cityId();
      if (cityId === null || !this.showDistrict()) {
        this.districts.set(IDLE);
        return;
      }
      onCleanup(this.load({ level: DgaGeoLevel.District, state: this.districts, read: () => source.districts(cityId) }));
    });
  }

  /** The text under a level: its load error, or the field error when it belongs there. */
  levelError(level: DgaGeoLevel): string | null {
    if (this.levelState(level)().failed) return this.loadErrorText();
    return this.errorLevel() === level ? this.error() : null;
  }

  onRegion(regionId: DgaGeoId | null): void {
    this.value.set(withGeoRegion(this.value(), regionId));
  }

  onCity(cityId: DgaGeoId | null): void {
    this.value.set(withGeoCity(this.value(), cityId));
  }

  onDistrict(districtId: DgaGeoId | null): void {
    this.value.set(withGeoDistrict(this.value(), districtId));
  }

  onTouched(touched: boolean): void {
    if (touched && !this.touched()) this.touched.set(true);
  }

  private levelState(level: DgaGeoLevel): WritableSignal<GeoLevelState> {
    if (level === DgaGeoLevel.Region) return this.regions;
    return level === DgaGeoLevel.City ? this.cities : this.districts;
  }

  /** Loads one level's list and returns the cancel function. The source is called untracked. */
  private load(request: {
    readonly level: DgaGeoLevel;
    readonly state: WritableSignal<GeoLevelState>;
    readonly read: () => DgaGeoResult<readonly DgaGeoPlace[]>;
  }): () => void {
    return untracked(() => {
      request.state.set(LOADING);
      return readGeoResult(request.read(), {
        next: (places) => request.state.set({ places, loading: false, failed: false }),
        error: () => {
          request.state.set(FAILED);
          this.loadFailed.emit(request.level);
        },
      });
    });
  }
}
