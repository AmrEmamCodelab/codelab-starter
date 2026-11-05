import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  DgaAlertComponent,
  DgaAlertVariant,
  DgaButtonComponent,
  DgaCardComponent,
  DgaIconComponent,
  DgaInfoFieldComponent,
} from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { APP_CLOCK } from '../../core/session/clock';
import {
  RequesterDetailActionsDirective,
  RequesterDetailAttachmentsDirective,
  RequesterDetailPatternComponent,
} from '../../patterns';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { SHOWCASE_API_ERROR, ShowcaseState } from '../frame/showcase-state';
import { ShowcaseStateSwitchComponent } from '../frame/showcase-state-switch.component';
import { ShowcasePageId } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { SAMPLE_PHONE, SAMPLE_RECORD_NUMBER, UPLOAD_FIXTURE } from '../components/component-fixtures';
import { createdAtFixture, historyFixture, historyTone } from './pattern-fixtures';
import { STAFF_DETAIL_STATES } from './staff-detail-page.component';

const P = ShowcaseTranslationKeys.Patterns;

/**
 * The requester detail pattern with its status tracker: the requester's own request after it was returned for
 * changes (banner), its summary, attachments, the status history and the actions the requester may take.
 */
@Component({
  selector: 'app-showcase-requester-detail-page',
  imports: [
    DgaAlertComponent,
    DgaButtonComponent,
    DgaCardComponent,
    DgaIconComponent,
    DgaInfoFieldComponent,
    RequesterDetailActionsDirective,
    RequesterDetailAttachmentsDirective,
    RequesterDetailPatternComponent,
    ShowcaseFrameComponent,
    ShowcaseStateSwitchComponent,
    TranslatePipe,
  ],
  templateUrl: './requester-detail-page.component.html',
  styleUrl: './pattern-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RequesterDetailPageComponent {
  private readonly translations = inject(TranslationService);
  private readonly clock = inject(APP_CLOCK);

  protected readonly pageId = ShowcasePageId.RequesterDetail;
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly states = STAFF_DETAIL_STATES;
  protected readonly recordNumber = SAMPLE_RECORD_NUMBER;
  protected readonly phone = SAMPLE_PHONE;
  protected readonly statusKey = P.HistoryResubmitted;
  protected readonly statusTone = historyTone(P.HistoryResubmitted);
  protected readonly toneFor = historyTone;
  protected readonly createdAt = createdAtFixture(this.clock);
  protected readonly attachments = UPLOAD_FIXTURE.filter(file => !('errorKey' in file));
  protected readonly AlertVariant = DgaAlertVariant;

  readonly state = signal<ShowcaseState>(ShowcaseState.Ready);

  protected readonly loading = computed(() => this.state() === ShowcaseState.Loading);
  protected readonly error = computed(() => (this.state() === ShowcaseState.Error ? SHOWCASE_API_ERROR : null));
  protected readonly history = computed(() => historyFixture(this.clock, key => this.translations.translate(key)));

  retry(): void {
    this.state.set(ShowcaseState.Ready);
  }
}
