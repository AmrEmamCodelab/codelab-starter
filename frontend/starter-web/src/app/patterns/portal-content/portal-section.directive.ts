import { Directive, computed, input } from '@angular/core';
import { sectionAnchorId } from './table-of-contents';

/**
 * Marks one section of a portal content page: `<section [portalSection]="'eligibility'">`. It gives the element the
 * anchor id the table of contents links to and makes it focusable from script only (`tabindex="-1"`), so following a
 * table-of-contents link moves keyboard and screen-reader focus to the section, not just the scroll position.
 */
@Directive({
  selector: '[portalSection]',
  host: {
    class: 'portal-section',
    tabindex: '-1',
    '[id]': 'anchorId()',
  },
})
export class PortalSectionDirective {
  /** The section id, as in the screen's `PortalSection` list. */
  readonly portalSection = input.required<string>();
  readonly anchorId = computed(() => sectionAnchorId(this.portalSection()));
}
