import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  DgaAlertComponent,
  DgaAlertVariant,
  DgaCardComponent,
  DgaIconComponent,
  DgaInfoFieldComponent,
  DgaStatusTagComponent,
} from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { APP_CLOCK } from '../../core/session/clock';
import {
  ActionTone,
  DecisionDialogComponent,
  StaffDetailPatternComponent,
  StatusTrackerComponent,
  StatusTrackerHeadingLevel,
  type AllowedAction,
} from '../../patterns';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { SHOWCASE_API_ERROR, ShowcaseState } from '../frame/showcase-state';
import { ShowcaseStateSwitchComponent } from '../frame/showcase-state-switch.component';
import { ShowcasePageId, showcaseLink } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { SAMPLE_PHONE, SAMPLE_RECORD_NUMBER } from '../components/component-fixtures';
import { ALLOWED_ACTIONS_FIXTURE, historyFixture, historyTone } from './pattern-fixtures';

/** The states the staff-detail demo offers (a record page has no empty state). */
export const STAFF_DETAIL_STATES: readonly ShowcaseState[] = [ShowcaseState.Ready, ShowcaseState.Loading, ShowcaseState.Error];

/**
 * The staff detail pattern with the decision dialog: detail cards, the status history, and the server's allowed
 * actions. An action that needs a reason opens the decision dialog; the others (and a confirmed reason) record the
 * decision and show it in an alert.
 */
@Component({
  selector: 'app-showcase-staff-detail-page',
  imports: [
    DecisionDialogComponent,
    DgaAlertComponent,
    DgaCardComponent,
    DgaIconComponent,
    DgaInfoFieldComponent,
    DgaStatusTagComponent,
    RouterLink,
    ShowcaseFrameComponent,
    ShowcaseStateSwitchComponent,
    StaffDetailPatternComponent,
    StatusTrackerComponent,
    TranslatePipe,
  ],
  templateUrl: './staff-detail-page.component.html',
  styleUrl: './pattern-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StaffDetailPageComponent {
  private readonly translations = inject(TranslationService);
  private readonly clock = inject(APP_CLOCK);

  protected readonly pageId = ShowcasePageId.StaffDetail;
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly states = STAFF_DETAIL_STATES;
  protected readonly queueLink = showcaseLink(ShowcasePageId.WorkQueue);
  protected readonly recordNumber = SAMPLE_RECORD_NUMBER;
  protected readonly phone = SAMPLE_PHONE;
  protected readonly toneFor = historyTone;
  protected readonly HeadingLevel = StatusTrackerHeadingLevel;
  protected readonly AlertVariant = DgaAlertVariant;
  protected readonly Tone = ActionTone;
  protected readonly titleParams = { reference: SAMPLE_RECORD_NUMBER };

  readonly state = signal<ShowcaseState>(ShowcaseState.Ready);
  /** The action waiting for its reason in the decision dialog, or null. */
  readonly pending = signal<AllowedAction | null>(null);
  readonly reason = signal('');
  /** The label key of the last decision taken, shown in an alert. */
  readonly decided = signal<string | null>(null);

  protected readonly actions = computed(() => (this.state() === ShowcaseState.Ready ? ALLOWED_ACTIONS_FIXTURE : []));
  protected readonly loading = computed(() => this.state() === ShowcaseState.Loading);
  protected readonly error = computed(() => (this.state() === ShowcaseState.Error ? SHOWCASE_API_ERROR : null));
  protected readonly history = computed(() => historyFixture(this.clock, key => this.translations.translate(key)));

  /** An action without a reason is decided at once; one that needs a reason opens the dialog first. */
  onDecide(action: AllowedAction): void {
    if (action.requiresReason) {
      this.reason.set('');
      this.pending.set(action);
      return;
    }
    this.decided.set(action.labelKey);
  }

  onConfirm(): void {
    this.decided.set(this.pending()?.labelKey ?? null);
    this.pending.set(null);
  }

  onCancel(): void {
    this.pending.set(null);
  }

  retry(): void {
    this.state.set(ShowcaseState.Ready);
  }
}
