import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DgaHeroSliderComponent, DgaIconComponent, type DgaHeroSlide } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { ShowcaseDemoComponent } from '../frame/showcase-demo.component';
import { ShowcaseSectionComponent } from '../frame/showcase-section.component';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { ICON_NAMES, SLIDE_FIXTURE } from './component-fixtures';

/** Gallery sections of the media components: dga-hero-slider (autoplay off) and every dga-icon glyph. */
@Component({
  selector: 'app-showcase-media-demos',
  imports: [DgaHeroSliderComponent, DgaIconComponent, ShowcaseDemoComponent, ShowcaseSectionComponent, TranslatePipe],
  templateUrl: './media-demos.component.html',
  styleUrl: './demos.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MediaDemosComponent {
  private readonly translations = inject(TranslationService);

  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly icons = ICON_NAMES;
  protected readonly slides = computed<DgaHeroSlide[]>(() => {
    const translate = (key: string) => this.translations.translate(key);
    return SLIDE_FIXTURE.map(slide => ({
      id: slide.id,
      title: translate(slide.titleKey),
      subtitle: translate(slide.subtitleKey),
      link: slide.link,
      linkLabel: translate(ShowcaseTranslationKeys.Demo.SliderLink),
    }));
  });

  readonly slideIndex = signal(0);
}
