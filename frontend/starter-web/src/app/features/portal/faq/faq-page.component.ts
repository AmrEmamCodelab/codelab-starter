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
import { PortalContentPatternComponent } from '../../../patterns';
import { PortalContentStatus } from '../content/portal-content';
import { PortalContentPages } from '../content/portal-content.client';
import { PortalContentStateComponent } from '../content/portal-content-state.component';
import { PortalContentStore } from '../content/portal-content.store';
import { PortalLinks, portalBreadcrumbs } from '../portal-links';
import { PortalCommonTranslationKeys } from '../portal-translation-keys';
import { filterFaq, searchTerms } from './faq-search';
import { FaqTranslationKeys } from './faq-translation-keys';

/**
 * `/portal/faq` (public): the frequently asked questions from `GET api/portal/content/faq` (one section per question:
 * the title is the question, the body the answer) in one `dga-accordion`, on the portal content pattern. The search box
 * filters the questions as the reader types (question and answer text in the active language, Arabic spelling variants
 * folded); the number of matches is announced politely. Loading, empty and failed content show as page feedback.
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
    PortalContentStateComponent,
    RouterLink,
    TranslatePipe,
  ],
  providers: [PortalContentStore],
  templateUrl: './faq-page.component.html',
  styleUrl: './faq-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FaqPageComponent {
  private readonly translations = inject(TranslationService);
  protected readonly content = inject(PortalContentStore);

  protected readonly keys = FaqTranslationKeys;
  protected readonly common = PortalCommonTranslationKeys;
  protected readonly contactLink = PortalLinks.Contact;
  protected readonly accordionMode = DgaAccordionMode.Multiple;
  protected readonly headingLevel = DgaAccordionHeadingLevel.H3;

  /** What the reader typed in the search box. */
  readonly query = signal('');
  readonly searching = computed(() => searchTerms(this.query()).length > 0);
  /** True once the questions are loaded and there is at least one (the search box shows only then). */
  readonly ready = computed(
    () => this.content.status() === PortalContentStatus.Loaded && this.content.sections().length > 0,
  );
  readonly questions = computed(() => filterFaq(this.content.sections(), this.query()));
  readonly matchCount = computed(() => this.questions().length);
  readonly breadcrumbs = computed(() =>
    portalBreadcrumbs(key => this.translations.translate(key), FaqTranslationKeys.Title),
  );

  constructor() {
    void this.content.load(PortalContentPages.Faq);
  }

  clearSearch(): void {
    this.query.set('');
  }
}
