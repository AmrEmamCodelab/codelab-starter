import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  DgaAccordionComponent,
  DgaAccordionHeadingLevel,
  DgaAccordionItemComponent,
  DgaAccordionMode,
  DgaButtonComponent,
  DgaIconComponent,
  DgaPortalPageHeaderComponent,
  DgaTextInputComponent,
} from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { PortalContentPatternComponent, PortalSectionDirective, type PortalSection } from '../../../patterns';
import { PortalLinks, portalBreadcrumbs } from '../portal-links';
import { PortalCommonTranslationKeys } from '../portal-translation-keys';
import { FAQ_CATEGORIES, faqEntryCount } from './faq-content';
import { filterFaq, searchTerms } from './faq-search';
import { FaqTranslationKeys } from './faq-translation-keys';

/**
 * `/portal/faq` (public): the frequently asked questions grouped by category, one `dga-accordion` per category, on the
 * portal content pattern. The search box filters the questions as the reader types (question and answer text, in the
 * active language, Arabic spelling variants folded); the number of matches is announced politely.
 */
@Component({
  selector: 'app-faq-page',
  imports: [
    DgaAccordionComponent,
    DgaAccordionItemComponent,
    DgaButtonComponent,
    DgaIconComponent,
    DgaPortalPageHeaderComponent,
    DgaTextInputComponent,
    PortalContentPatternComponent,
    PortalSectionDirective,
    RouterLink,
    TranslatePipe,
  ],
  templateUrl: './faq-page.component.html',
  styleUrl: './faq-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FaqPageComponent {
  private readonly translations = inject(TranslationService);

  protected readonly keys = FaqTranslationKeys;
  protected readonly common = PortalCommonTranslationKeys;
  protected readonly contactLink = PortalLinks.Contact;
  protected readonly accordionMode = DgaAccordionMode.Multiple;
  protected readonly headingLevel = DgaAccordionHeadingLevel.H3;

  /** What the reader typed in the search box. */
  readonly query = signal('');
  readonly searching = computed(() => searchTerms(this.query()).length > 0);
  readonly categories = computed(() =>
    filterFaq(FAQ_CATEGORIES, this.query(), key => this.translations.translate(key)),
  );
  readonly matchCount = computed(() => faqEntryCount(this.categories()));
  readonly sections = computed<PortalSection[]>(() =>
    this.categories().map(category => ({ id: category.id, titleKey: category.titleKey })),
  );
  readonly breadcrumbs = computed(() =>
    portalBreadcrumbs(key => this.translations.translate(key), FaqTranslationKeys.Title),
  );

  clearSearch(): void {
    this.query.set('');
  }
}
