import { booleanAttribute, ChangeDetectionStrategy, Component, computed, ElementRef, inject, input, model, viewChild } from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { createDgaId } from '../internal/unique-id';
import { DGA_ACCORDION_DEFAULT_HEADING_LEVEL, DGA_ACCORDION_PARENT, type DgaAccordionItemRef } from './accordion-navigation';

/**
 * One section of a <dga-accordion>: a heading that holds a native toggle button, and a labelled region with the
 * projected content.
 *
 * - `expanded` is a signal model: bind [(expanded)] to open an item from code or to follow it.
 * - The header button carries `aria-expanded` and `aria-controls`; the panel is `role="region"` labelled by the
 *   header and is hidden while collapsed.
 * - `label` is the header text (already translated); the body is projected content.
 * - Works on its own too (heading level 3), but keyboard movement between headers needs the accordion.
 *
 * @example
 * <dga-accordion-item [label]="'faq.q1' | translate" [(expanded)]="firstOpen">{{ 'faq.a1' | translate }}</dga-accordion-item>
 */
@Component({
  selector: 'dga-accordion-item',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-accordion-item.component.html',
  styleUrl: './dga-accordion-item.component.css',
  host: { class: 'dga-accordion-item', '[class.dga-accordion-item--expanded]': 'expanded()' },
})
export class DgaAccordionItemComponent implements DgaAccordionItemRef {
  private readonly parent = inject(DGA_ACCORDION_PARENT, { optional: true });
  private readonly headerButton = viewChild.required<ElementRef<HTMLButtonElement>>('headerButton');

  readonly expanded = model(false);
  /** The header text (already translated). */
  readonly label = input.required<string>();
  readonly disabled = input(false, { transform: booleanAttribute });

  readonly headerId = createDgaId('dga-accordion-header');
  readonly panelId = `${this.headerId}-panel`;
  readonly headingLevel = computed(() => this.parent?.headingLevel() ?? DGA_ACCORDION_DEFAULT_HEADING_LEVEL);

  toggle(): void {
    if (this.disabled()) return;
    const next = !this.expanded();
    this.expanded.set(next);
    this.parent?.itemToggled(this, next);
  }

  collapse(): void {
    if (this.expanded()) this.expanded.set(false);
  }

  focusHeader(): void {
    this.headerButton().nativeElement.focus();
  }

  isDisabled(): boolean {
    return this.disabled();
  }

  onHeaderKeydown(event: KeyboardEvent): void {
    if (this.parent?.moveFocus(this, event.key)) event.preventDefault();
  }
}
