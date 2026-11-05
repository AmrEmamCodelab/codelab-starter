import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  DgaDateInputComponent,
  DgaFileUploadComponent,
  DgaGeoDepth,
  DgaGeoSelectComponent,
  DgaHijriDateInputComponent,
  DgaUnitInputComponent,
  type DgaFileSelection,
  type DgaFileUploadItem,
  type DgaGeoSelection,
} from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslationService } from '../../core/i18n/translation.service';
import { APP_CLOCK } from '../../core/session/clock';
import { GEO_DATA_SOURCE } from '../../shared/geo-select/geo-data-source';
import { ShowcaseDemoComponent } from '../frame/showcase-demo.component';
import { ShowcaseSectionComponent } from '../frame/showcase-section.component';
import { showcaseToday } from '../showcase-clock';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import {
  SAMPLE_HIJRI_DATE,
  SAMPLE_WEIGHT_KG,
  UPLOAD_FIXTURE,
  UPLOAD_MAX_BYTES,
  UPLOAD_MAX_FILES,
  WEIGHT_MAX_KG,
  WEIGHT_MIN_KG,
  appendPicked,
  removeItem,
} from './component-fixtures';

/** The file types the upload demo accepts. */
export const UPLOAD_ACCEPT = '.pdf,.png,.jpg,.jpeg';

const NO_PLACE: DgaGeoSelection = { regionId: null, cityId: null, districtId: null };

/**
 * Gallery sections of the specialised pickers: Gregorian and Hijri dates (on the pinned showcase clock), the unit
 * input, the region/city/district cascade over the app's static geo source, and the presentational file upload.
 */
@Component({
  selector: 'app-showcase-form-picker-demos',
  imports: [
    DgaDateInputComponent,
    DgaFileUploadComponent,
    DgaGeoSelectComponent,
    DgaHijriDateInputComponent,
    DgaUnitInputComponent,
    ShowcaseDemoComponent,
    ShowcaseSectionComponent,
    TranslatePipe,
  ],
  templateUrl: './form-picker-demos.component.html',
  styleUrl: './demos.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FormPickerDemosComponent {
  private readonly translations = inject(TranslationService);

  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly language = inject(LanguageService).current;
  protected readonly geoData = inject(GEO_DATA_SOURCE);
  protected readonly today = showcaseToday(inject(APP_CLOCK));
  protected readonly GeoDepth = DgaGeoDepth;
  protected readonly accept = UPLOAD_ACCEPT;
  protected readonly maxBytes = UPLOAD_MAX_BYTES;
  protected readonly maxFiles = UPLOAD_MAX_FILES;
  protected readonly weightMin = WEIGHT_MIN_KG;
  protected readonly weightMax = WEIGHT_MAX_KG;

  readonly visitDate = signal('');
  readonly birthDate = signal('');
  readonly hijriDate = signal(SAMPLE_HIJRI_DATE);
  readonly weight = signal<number | null>(SAMPLE_WEIGHT_KG);
  readonly place = signal<DgaGeoSelection>(NO_PLACE);
  readonly picked = signal<readonly DgaFileUploadItem[]>([]);

  /** The fixture rows (translated) followed by whatever the user picked in this visit. */
  protected readonly uploads = computed<DgaFileUploadItem[]>(() => {
    const translate = (key: string) => this.translations.translate(key);
    const fixture = UPLOAD_FIXTURE.map(row => ({
      id: row.id,
      name: row.name,
      status: row.status,
      progress: 'progress' in row ? row.progress : undefined,
      detail: 'detailKey' in row ? translate(row.detailKey) : undefined,
      errorText: 'errorKey' in row ? translate(row.errorKey) : undefined,
    }));
    return [...fixture, ...this.picked()];
  });

  onFilesSelected(selection: DgaFileSelection): void {
    this.picked.update(items => appendPicked(items, selection.accepted));
  }

  onRemoved(id: string): void {
    this.picked.update(items => removeItem(items, id));
  }
}
