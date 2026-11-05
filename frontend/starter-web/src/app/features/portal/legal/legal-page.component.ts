import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaPortalPageHeaderComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { PortalContentPatternComponent, PortalSectionDirective } from '../../../patterns';
import { PortalLinks, portalBreadcrumbs } from '../portal-links';
import { PortalCommonTranslationKeys } from '../portal-translation-keys';
import { legalTocSections, type LegalDocument } from './legal-content';

/**
 * `/portal/privacy` and `/portal/terms` (public): a legal document on the portal content pattern. The route passes the
 * document (`PRIVACY_DOCUMENT` or `TERMS_DOCUMENT`) as route data, bound to {@link document}; the page renders its
 * sections in order with a table of contents and a closing link to the contact page.
 */
@Component({
  selector: 'app-legal-page',
  imports: [DgaPortalPageHeaderComponent, PortalContentPatternComponent, PortalSectionDirective, RouterLink, TranslatePipe],
  templateUrl: './legal-page.component.html',
  styleUrl: './legal-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LegalPageComponent {
  readonly document = input.required<LegalDocument>();

  private readonly translations = inject(TranslationService);
  protected readonly common = PortalCommonTranslationKeys;
  protected readonly contactLink = PortalLinks.Contact;

  readonly sections = computed(() => legalTocSections(this.document()));
  readonly breadcrumbs = computed(() =>
    portalBreadcrumbs(key => this.translations.translate(key), this.document().titleKey),
  );
}
