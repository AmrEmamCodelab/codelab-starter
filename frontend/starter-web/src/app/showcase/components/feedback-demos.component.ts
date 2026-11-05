import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  DgaAlertComponent,
  DgaAlertVariant,
  DgaButtonComponent,
  DgaConfirmDialogComponent,
  DgaConfirmDialogVariant,
  DgaErrorDialogComponent,
  DgaPageFeedbackComponent,
  DgaPageFeedbackKind,
  type DgaPageFeedbackState,
} from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { ShowcaseDemoComponent } from '../frame/showcase-demo.component';
import { ShowcaseSectionComponent } from '../frame/showcase-section.component';
import { SHOWCASE_API_ERROR } from '../frame/showcase-state';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';

const Demo = ShowcaseTranslationKeys.Demo;

/** Which demo dialog is open (one at a time). */
export const FeedbackDialog = {
  Confirm: 'confirm',
  Danger: 'danger',
  Error: 'error',
} as const;

export type FeedbackDialog = (typeof FeedbackDialog)[keyof typeof FeedbackDialog];

/**
 * Gallery sections of the feedback components: every dga-alert variant, the two dialogs (opened from buttons, so
 * the page never starts behind a modal) and the page-feedback banner in each kind (focus move off, so the static
 * banners do not steal focus on load).
 */
@Component({
  selector: 'app-showcase-feedback-demos',
  imports: [
    DgaAlertComponent,
    DgaButtonComponent,
    DgaConfirmDialogComponent,
    DgaErrorDialogComponent,
    DgaPageFeedbackComponent,
    ShowcaseDemoComponent,
    ShowcaseSectionComponent,
    TranslatePipe,
  ],
  templateUrl: './feedback-demos.component.html',
  styleUrl: './demos.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FeedbackDemosComponent {
  private readonly translations = inject(TranslationService);

  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly Variant = DgaAlertVariant;
  protected readonly ConfirmVariant = DgaConfirmDialogVariant;
  protected readonly Dialog = FeedbackDialog;
  protected readonly referenceCode = SHOWCASE_API_ERROR.referenceCode ?? null;

  readonly openDialog = signal<FeedbackDialog | null>(null);
  readonly alertDismissed = signal(false);

  /** One banner per kind, each with the demo label it shows under; dismissing one hides it. */
  protected readonly feedbackStates = computed<{ readonly labelKey: string; readonly state: DgaPageFeedbackState }[]>(() => {
    const translate = (key: string) => this.translations.translate(key);
    const States = ShowcaseTranslationKeys.States;
    return [
      {
        labelKey: States.Success,
        state: { kind: DgaPageFeedbackKind.Success, title: translate(Demo.FeedbackSuccessTitle), message: translate(Demo.FeedbackSuccess) },
      },
      { labelKey: States.Error, state: { kind: DgaPageFeedbackKind.Error, message: translate(Demo.FeedbackError) } },
      { labelKey: States.Info, state: { kind: DgaPageFeedbackKind.Info, message: translate(Demo.FeedbackInfo) } },
    ].filter(entry => !this.dismissedKinds().has(entry.state.kind));
  });

  readonly dismissedKinds = signal<ReadonlySet<string>>(new Set());

  dismissFeedback(kind: string): void {
    this.dismissedKinds.update(kinds => new Set([...kinds, kind]));
  }

  open(dialog: FeedbackDialog): void {
    this.openDialog.set(dialog);
  }

  close(): void {
    this.openDialog.set(null);
  }
}
