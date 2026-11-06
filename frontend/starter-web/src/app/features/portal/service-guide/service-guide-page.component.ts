import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaIconComponent, DgaPortalPageHeaderComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { PortalContentPatternComponent, PortalSectionDirective, type PortalSection } from '../../../patterns';
import { contentTocSections } from '../content/portal-content';
import { PortalContentPages } from '../content/portal-content.client';
import { PortalContentStateComponent } from '../content/portal-content-state.component';
import { PortalContentStore } from '../content/portal-content.store';
import { PortalLinks, portalBreadcrumbs } from '../portal-links';
import { PortalCommonTranslationKeys } from '../portal-translation-keys';
import { ServiceGuideSectionIds } from './service-guide-content';
import { ServiceGuideTranslationKeys } from './service-guide-translation-keys';

/**
 * `/portal/service-guide` (public): the guide's sections from `GET api/portal/content/service-guide` (title and plain
 * text paragraphs in the active language) on the portal content pattern, with a table of contents and a closing help
 * block linking to the FAQ and contact pages. Loading, empty and failed content show as page feedback.
 */
@Component({
  selector: 'app-service-guide-page',
  imports: [
    DgaIconComponent,
    DgaPortalPageHeaderComponent,
    PortalContentPatternComponent,
    PortalContentStateComponent,
    PortalSectionDirective,
    RouterLink,
    TranslatePipe,
  ],
  providers: [PortalContentStore],
  templateUrl: './service-guide-page.component.html',
  styleUrl: './service-guide-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceGuidePageComponent {
  private readonly translations = inject(TranslationService);
  protected readonly content = inject(PortalContentStore);

  protected readonly keys = ServiceGuideTranslationKeys;
  protected readonly common = PortalCommonTranslationKeys;
  protected readonly ids = ServiceGuideSectionIds;
  protected readonly links = PortalLinks;

  /** The table of contents: the API sections, then the help block. */
  readonly tocSections = computed<PortalSection[]>(() => [
    ...contentTocSections(this.content.sections()),
    { id: ServiceGuideSectionIds.Help, titleKey: ServiceGuideTranslationKeys.HelpTitle },
  ]);
  readonly breadcrumbs = computed(() =>
    portalBreadcrumbs(key => this.translations.translate(key), ServiceGuideTranslationKeys.Title),
  );

  constructor() {
    void this.content.load(PortalContentPages.ServiceGuide);
  }
}
