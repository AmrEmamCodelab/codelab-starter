import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  DgaButtonComponent,
  DgaCheckboxComponent,
  DgaInfoFieldComponent,
  DgaStatusTagComponent,
  DgaStepperComponent,
  DgaTextInputComponent,
  type DgaStep,
} from '@codelab/ng-dga';
import { AppConfigService } from '../../core/config/app-config.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { STEP_FIXTURE, withLabels } from '../components/component-fixtures';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { ShowcaseHeadingLevel, ShowcaseSectionComponent } from '../frame/showcase-section.component';
import { ShowcasePageId, showcaseLink } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { BrandingPreview, SAMPLE_BRAND_COLOURS } from './branding-preview';

/** The anchors of the branding page sections. */
export const BrandingSection = { Current: 'branding-current', Preview: 'branding-preview', Samples: 'branding-samples' } as const;

/**
 * The project branding from `config.json` (`init.ps1 -PrimaryColor -LogoPath -NameEn -NameAr` writes it), the
 * components that follow the brand colour, and a live preview of other colours (reset when the page is left).
 */
@Component({
  selector: 'app-showcase-branding-page',
  imports: [
    DgaButtonComponent,
    DgaCheckboxComponent,
    DgaInfoFieldComponent,
    DgaStatusTagComponent,
    DgaStepperComponent,
    DgaTextInputComponent,
    RouterLink,
    ShowcaseFrameComponent,
    ShowcaseSectionComponent,
    TranslatePipe,
  ],
  providers: [BrandingPreview],
  templateUrl: './branding-page.component.html',
  styleUrl: './branding-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BrandingPageComponent {
  private readonly translations = inject(TranslationService);

  protected readonly pageId = ShowcasePageId.Branding;
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly Section = BrandingSection;
  protected readonly Level = ShowcaseHeadingLevel;
  protected readonly branding = inject(AppConfigService).branding ?? null;
  protected readonly colours = SAMPLE_BRAND_COLOURS;
  protected readonly preview = inject(BrandingPreview);
  protected readonly galleryLink = showcaseLink(ShowcasePageId.Components);
  protected readonly steps = computed<DgaStep[]>(() => withLabels(STEP_FIXTURE, key => this.translations.translate(key)));

  readonly checked = signal(true);
  readonly text = signal('');
}
