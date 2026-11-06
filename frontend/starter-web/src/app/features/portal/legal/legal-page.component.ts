import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaPortalPageHeaderComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { PortalContentPatternComponent, PortalSectionDirective } from '../../../patterns';
import { contentTocSections } from '../content/portal-content';
import { PortalContentStateComponent } from '../content/portal-content-state.component';
import { PortalContentStore } from '../content/portal-content.store';
import { PortalLinks, portalBreadcrumbs } from '../portal-links';
import { PortalCommonTranslationKeys } from '../portal-translation-keys';
import type { LegalDocument } from './legal-content';

/**
 * `/portal/privacy` and `/portal/terms` (public): a legal document on the portal content pattern. The route passes the
 * document (`PRIVACY_DOCUMENT` or `TERMS_DOCUMENT`) as route data, bound to {@link document}; the page loads its
 * sections from `GET api/portal/content/{page}` and renders them in order (plain text paragraphs in the active
 * language) with a table of contents and a closing link to the contact page. Loading, empty and failed content show
 * as page feedback.
 */
@Component({
  selector: 'app-legal-page',
  imports: [
    DgaPortalPageHeaderComponent,
    PortalContentPatternComponent,
    PortalContentStateComponent,
    PortalSectionDirective,
    RouterLink,
    TranslatePipe,
  ],
  providers: [PortalContentStore],
  templateUrl: './legal-page.component.html',
  styleUrl: './legal-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LegalPageComponent {
  readonly document = input.required<LegalDocument>();

  private readonly translations = inject(TranslationService);
  protected readonly content = inject(PortalContentStore);
  protected readonly common = PortalCommonTranslationKeys;
  protected readonly contactLink = PortalLinks.Contact;

  readonly sections = computed(() => contentTocSections(this.content.sections()));
  readonly breadcrumbs = computed(() =>
    portalBreadcrumbs(key => this.translations.translate(key), this.document().titleKey),
  );

  constructor() {
    effect(() => {
      const page = this.document().page;
      untracked(() => void this.content.load(page));
    });
  }
}
