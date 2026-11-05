import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaAlertComponent, DgaAlertVariant, DgaIconComponent, DgaPortalPageHeaderComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { PortalContentPatternComponent, PortalSectionDirective } from '../../../patterns';
import { PortalLinks, portalBreadcrumbs } from '../portal-links';
import { PortalCommonTranslationKeys } from '../portal-translation-keys';
import {
  DOCUMENT_ITEM_KEYS,
  ELIGIBILITY_ITEM_KEYS,
  GUIDE_STEPS,
  OVERVIEW_PARAGRAPH_KEYS,
  SERVICE_GUIDE_SECTIONS,
  ServiceGuideSectionIds,
  stepNumber,
} from './service-guide-content';
import { ServiceGuideTranslationKeys } from './service-guide-translation-keys';

/**
 * `/portal/service-guide` (public): what the service is, who can apply, the documents to prepare, the numbered steps
 * and where to get help, on the portal content pattern. The content is the typed key lists of
 * `service-guide-content.ts`; the text is static i18n now and settings data from P5.
 */
@Component({
  selector: 'app-service-guide-page',
  imports: [
    DgaAlertComponent,
    DgaIconComponent,
    DgaPortalPageHeaderComponent,
    PortalContentPatternComponent,
    PortalSectionDirective,
    RouterLink,
    TranslatePipe,
  ],
  templateUrl: './service-guide-page.component.html',
  styleUrl: './service-guide-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceGuidePageComponent {
  private readonly translations = inject(TranslationService);

  protected readonly keys = ServiceGuideTranslationKeys;
  protected readonly common = PortalCommonTranslationKeys;
  protected readonly ids = ServiceGuideSectionIds;
  protected readonly links = PortalLinks;
  protected readonly alertVariant = DgaAlertVariant.Warning;
  protected readonly sections = SERVICE_GUIDE_SECTIONS;
  protected readonly overview = OVERVIEW_PARAGRAPH_KEYS;
  protected readonly eligibility = ELIGIBILITY_ITEM_KEYS;
  protected readonly documents = DOCUMENT_ITEM_KEYS;
  protected readonly steps = GUIDE_STEPS;

  readonly breadcrumbs = computed(() =>
    portalBreadcrumbs(key => this.translations.translate(key), ServiceGuideTranslationKeys.Title),
  );

  /** The step's number in the active language's digits. */
  numberOf(index: number): string {
    return stepNumber(index, this.translations.language());
  }
}
