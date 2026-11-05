import {
  afterRenderEffect,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  input,
  output,
  untracked,
  viewChild,
} from '@angular/core';
import { DgaButtonComponent } from '../button/dga-button.component';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { hasText } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';
import { type DgaPageFeedbackState, pageFeedbackIcon, pageFeedbackRole } from './page-feedback-state';

/**
 * The DGA page feedback banner: the outcome of a page-level action (saved, sent, failed) shown inline at the top of
 * the page content.
 *
 * - Bound to `state`: null renders nothing; each new state object renders the banner in its tone, with
 *   `role="alert"` for an error and `role="status"` otherwise, and moves keyboard focus to it (the banner is
 *   programmatically focusable, `tabindex="-1"`), so keyboard and screen-reader users land on the outcome. Set
 *   `moveFocus` to false for a banner that appears while the user is typing.
 * - With `dismissLabel` (the close button's accessible name) it can be closed: it emits `dismissed`, and the caller
 *   sets `state` to null.
 * - Every text arrives translated, inside the state.
 *
 * @example
 * <dga-page-feedback [state]="feedback()" [dismissLabel]="'common.close' | t" (dismissed)="feedback.set(null)" />
 * // feedback.set({ kind: DgaPageFeedbackKind.Success, message: translate('contact.sent') });
 */
@Component({
  selector: 'dga-page-feedback',
  imports: [DgaButtonComponent, DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-page-feedback.component.html',
  styleUrl: './dga-page-feedback.component.css',
})
export class DgaPageFeedbackComponent {
  readonly state = input<DgaPageFeedbackState | null>(null);
  /** Accessible name of the close button (already translated); without it the banner has no close button. */
  readonly dismissLabel = input<string | null>(null);
  /** Move focus to the banner whenever a new state arrives. */
  readonly moveFocus = input(true, { transform: booleanAttribute });
  readonly dismissed = output<void>();

  private readonly banner = viewChild<ElementRef<HTMLElement>>('banner');

  readonly titleId = createDgaId('dga-page-feedback-title');
  readonly role = computed(() => {
    const state = this.state();
    return state ? pageFeedbackRole(state.kind) : null;
  });
  readonly icon = computed(() => {
    const state = this.state();
    return state ? pageFeedbackIcon(state.kind) : null;
  });
  readonly hasTitle = computed(() => hasText(this.state()?.title));
  readonly dismissible = computed(() => hasText(this.dismissLabel()));

  constructor() {
    afterRenderEffect({
      write: () => {
        const state = this.state();
        const banner = this.banner();
        if (state && banner && untracked(() => this.moveFocus())) banner.nativeElement.focus();
      },
    });
  }

  dismiss(): void {
    this.dismissed.emit();
  }
}
