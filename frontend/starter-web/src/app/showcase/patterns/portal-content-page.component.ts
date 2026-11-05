import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import {
  DgaAccordionComponent,
  DgaAccordionHeadingLevel,
  DgaAccordionItemComponent,
  DgaPortalPageHeaderComponent,
} from '@codelab/ng-dga';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { APP_CLOCK } from '../../core/session/clock';
import { PortalContentPatternComponent, PortalSectionDirective } from '../../patterns';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { formatShowcaseDate } from '../showcase-clock';
import { ShowcasePageId } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { BREADCRUMB_FIXTURE, withLabels } from '../components/component-fixtures';
import { PORTAL_SECTIONS } from './pattern-fixtures';

/** The section ids of the portal-content demo (they match {@link PORTAL_SECTIONS}). */
export const PortalDemoSection = { About: 'about', Steps: 'steps', StepsOnline: 'steps-online', Documents: 'documents' } as const;

/**
 * The portal content pattern: a page header with breadcrumbs, the table of contents (with a subsection), sections of
 * static content with an FAQ accordion, and the last-updated line (masked in the visual baselines, as a date).
 */
@Component({
  selector: 'app-showcase-portal-content-page',
  imports: [
    DgaAccordionComponent,
    DgaAccordionItemComponent,
    DgaPortalPageHeaderComponent,
    PortalContentPatternComponent,
    PortalSectionDirective,
    ShowcaseFrameComponent,
    TranslatePipe,
  ],
  templateUrl: './portal-content-page.component.html',
  styleUrl: './pattern-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PortalContentPageComponent {
  private readonly translations = inject(TranslationService);
  private readonly language = inject(LanguageService);
  private readonly clock = inject(APP_CLOCK);

  protected readonly pageId = ShowcasePageId.PortalContent;
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly Section = PortalDemoSection;
  protected readonly sections = PORTAL_SECTIONS;
  protected readonly HeadingLevel = DgaAccordionHeadingLevel;
  protected readonly breadcrumbs = computed(() => withLabels(BREADCRUMB_FIXTURE, key => this.translations.translate(key)));
  protected readonly lastUpdated = computed(() => formatShowcaseDate(this.clock(), this.language.current()));
}
