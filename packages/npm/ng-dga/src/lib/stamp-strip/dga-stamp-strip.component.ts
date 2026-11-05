import { ChangeDetectionStrategy, Component, ElementRef, input, model, viewChild } from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { createDgaId } from '../internal/unique-id';

const ESCAPE_KEY = 'Escape';

/**
 * The DGA digital stamp strip: the thin band above the header that tells visitors this is an official government
 * website, with a disclosure button that reveals how to verify it. Both texts arrive translated (`text`,
 * `verifyLabel`); the verification details are projected content, shown in a region the button controls
 * (`aria-expanded`, `aria-controls`). Escape inside the details closes them and returns focus to the button.
 * `expanded` is two-way bindable.
 *
 * @example
 * <dga-stamp-strip [text]="'layouts.stamp.text' | t" [verifyLabel]="'layouts.stamp.verify' | t">
 *   <p>{{ 'layouts.stamp.domain' | t }}</p>
 *   <p>{{ 'layouts.stamp.https' | t }}</p>
 * </dga-stamp-strip>
 */
@Component({
  selector: 'dga-stamp-strip',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-stamp-strip.component.html',
  styleUrl: './dga-stamp-strip.component.css',
  host: {
    class: 'dga-stamp-strip',
    '[class.dga-stamp-strip--expanded]': 'expanded()',
  },
})
export class DgaStampStripComponent {
  private readonly toggleButton = viewChild.required<ElementRef<HTMLButtonElement>>('toggleButton');
  private readonly baseId = createDgaId('dga-stamp-strip');

  /** The statement, e.g. "An official website of the Kingdom of Saudi Arabia" (already translated). */
  readonly text = input.required<string>();
  /** The disclosure button label, e.g. "How do I verify?" (already translated). */
  readonly verifyLabel = input.required<string>();
  readonly expanded = model(false);

  readonly detailsId = `${this.baseId}-details`;

  toggle(): void {
    this.expanded.update((open) => !open);
  }

  onDetailsKeydown(event: KeyboardEvent): void {
    if (event.key !== ESCAPE_KEY || !this.expanded()) return;
    event.preventDefault();
    this.expanded.set(false);
    this.toggleButton().nativeElement.focus();
  }
}
