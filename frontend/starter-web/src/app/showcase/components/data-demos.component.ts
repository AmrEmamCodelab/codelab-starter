import { ChangeDetectionStrategy, Component } from '@angular/core';
import { DgaInfoFieldComponent, DgaStatusTagComponent, DgaTagComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ShowcaseDemoComponent } from '../frame/showcase-demo.component';
import { ShowcaseSectionComponent } from '../frame/showcase-section.component';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { SAMPLE_PHONE, SAMPLE_RECORD_NUMBER, STATUS_TONE_FIXTURE, TAG_TONE_FIXTURE } from './component-fixtures';

/** Gallery sections of the data-display components: dga-info-field, dga-status-tag and dga-tag. */
@Component({
  selector: 'app-showcase-data-demos',
  imports: [DgaInfoFieldComponent, DgaStatusTagComponent, DgaTagComponent, ShowcaseDemoComponent, ShowcaseSectionComponent, TranslatePipe],
  templateUrl: './data-demos.component.html',
  styleUrl: './demos.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DataDemosComponent {
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly statusTones = STATUS_TONE_FIXTURE;
  protected readonly tagTones = TAG_TONE_FIXTURE;
  protected readonly recordNumber = SAMPLE_RECORD_NUMBER;
  protected readonly phone = SAMPLE_PHONE;
}
