import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { DgaButtonComponent, DgaPageFeedbackComponent, DgaPageFeedbackKind, type DgaPageFeedbackState } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { PortalContentStatus } from './portal-content';
import { PortalContentTranslationKeys as Keys } from './portal-content-translation-keys';

/** The banner for a content status: loading and empty are informational, a failure is an error; loaded shows none. */
export function contentFeedback(
  status: PortalContentStatus,
  empty: boolean,
  translate: (key: string) => string,
): DgaPageFeedbackState | null {
  switch (status) {
    case PortalContentStatus.Loading:
      return { kind: DgaPageFeedbackKind.Info, message: translate(Keys.Loading) };
    case PortalContentStatus.Failed:
      return { kind: DgaPageFeedbackKind.Error, title: translate(Keys.FailedTitle), message: translate(Keys.FailedBody) };
    default:
      return empty ? { kind: DgaPageFeedbackKind.Info, message: translate(Keys.Empty) } : null;
  }
}

/**
 * The loading, empty and error states of a portal page whose content comes from the API, in a `dga-page-feedback`
 * banner (focus stays where it is: the banner appears on its own, not after a reader action), plus a retry button
 * after a failure. Renders nothing once the content is loaded and not empty.
 */
@Component({
  selector: 'app-portal-content-state',
  imports: [DgaButtonComponent, DgaPageFeedbackComponent, TranslatePipe],
  templateUrl: './portal-content-state.component.html',
  styleUrl: './portal-content-state.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PortalContentStateComponent {
  private readonly translations = inject(TranslationService);

  readonly status = input.required<PortalContentStatus>();
  /** True when the content loaded but holds no section. */
  readonly empty = input(false);
  readonly retry = output();

  protected readonly keys = Keys;
  readonly failed = computed(() => this.status() === PortalContentStatus.Failed);
  readonly feedback = computed(() =>
    contentFeedback(this.status(), this.empty(), key => this.translations.translate(key)),
  );
}
