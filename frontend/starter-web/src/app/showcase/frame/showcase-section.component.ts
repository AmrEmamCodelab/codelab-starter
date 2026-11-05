import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/** The heading level of a showcase section: a page section (h2) or a component inside a category (h3). */
export const ShowcaseHeadingLevel = {
  Section: 2,
  Subsection: 3,
} as const;

export type ShowcaseHeadingLevel = (typeof ShowcaseHeadingLevel)[keyof typeof ShowcaseHeadingLevel];

/** The id suffix of a section heading (`aria-labelledby` of the section). */
export const SECTION_HEADING_SUFFIX = '-heading';

/**
 * One anchored showcase section. The `anchor` is the element id the docs link to: a component section uses the
 * component's selector (`/showcase#dga-button`, as the generated catalog links) and shows it as code; any other
 * section passes a translated `titleKey`.
 */
@Component({
  selector: 'app-showcase-section',
  imports: [NgTemplateOutlet, TranslatePipe],
  templateUrl: './showcase-section.component.html',
  styleUrl: './showcase-section.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShowcaseSectionComponent {
  readonly anchor = input.required<string>();
  readonly titleKey = input<string | null>(null);
  readonly level = input<ShowcaseHeadingLevel>(ShowcaseHeadingLevel.Subsection);

  protected readonly Levels = ShowcaseHeadingLevel;
  protected readonly headingId = computed(() => `${this.anchor()}${SECTION_HEADING_SUFFIX}`);
}
