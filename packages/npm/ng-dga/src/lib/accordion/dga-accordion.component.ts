import { ChangeDetectionStrategy, Component, contentChildren, forwardRef, input } from '@angular/core';
import {
  DGA_ACCORDION_DEFAULT_HEADING_LEVEL,
  DGA_ACCORDION_PARENT,
  DgaAccordionMode,
  type DgaAccordionHeadingLevel,
  type DgaAccordionItemRef,
  type DgaAccordionParent,
  nextAccordionHeaderIndex,
} from './accordion-navigation';
import { DgaAccordionItemComponent } from './dga-accordion-item.component';

/**
 * The DGA accordion: a stack of <dga-accordion-item> sections (WAI-ARIA accordion pattern).
 *
 * - `mode` is `single` (opening an item closes the others; the default) or `multiple`. Single mode acts on user
 *   toggles; an app that opens items through their `expanded` model keeps them consistent itself.
 * - Keyboard: Up and Down move focus between headers (wrapping), Home and End jump to the first and last; disabled
 *   headers are skipped. Enter and Space toggle (native buttons).
 * - `headingLevel` sets the `aria-level` of every item heading to fit the page outline (default 3).
 * - Items are direct content children (control flow such as `@for` is fine); a nested accordion keeps its own items.
 *
 * @example
 * <dga-accordion mode="multiple">
 *   @for (entry of faq(); track entry.id) {
 *     <dga-accordion-item [label]="entry.question">{{ entry.answer }}</dga-accordion-item>
 *   }
 * </dga-accordion>
 */
@Component({
  selector: 'dga-accordion',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-accordion.component.html',
  styleUrl: './dga-accordion.component.css',
  host: { class: 'dga-accordion', '[attr.data-mode]': 'mode()' },
  providers: [{ provide: DGA_ACCORDION_PARENT, useExisting: forwardRef(() => DgaAccordionComponent) }],
})
export class DgaAccordionComponent implements DgaAccordionParent {
  private readonly items = contentChildren(DgaAccordionItemComponent);

  readonly mode = input<DgaAccordionMode>(DgaAccordionMode.Single);
  readonly headingLevel = input<DgaAccordionHeadingLevel>(DGA_ACCORDION_DEFAULT_HEADING_LEVEL);

  itemToggled(item: DgaAccordionItemRef, expanded: boolean): void {
    if (!expanded || this.mode() !== DgaAccordionMode.Single) return;
    for (const other of this.items()) {
      if (other !== item) other.collapse();
    }
  }

  moveFocus(item: DgaAccordionItemRef, key: string): boolean {
    const items = this.items();
    const next = nextAccordionHeaderIndex(
      items.map((entry) => entry.isDisabled()),
      items.findIndex((entry) => entry === item),
      key,
    );
    if (next === null) return false;
    items[next].focusHeader();
    return true;
  }
}
