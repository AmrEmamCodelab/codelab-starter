import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaCardComponent, DgaHeroComponent, DgaHeroSliderComponent, DgaHeroVariant, DgaIconComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { LandingDashboardPatternComponent } from '../../../patterns';
import { PortalLinks } from '../portal-links';
import { LANDING_QUICK_ACTIONS, LANDING_SLIDES, LANDING_TILES, SERVICE_CARDS, heroSlides } from './landing-content';
import { LandingTranslationKeys } from './landing-translation-keys';

/**
 * `/portal` (public): the portal's front page on the landing dashboard pattern. A brand hero with the page `h1` and
 * links to the guide and the contact page, a highlights slider (no autoplay), the service cards, public figure tiles
 * and quick links to the legal pages. All content is the typed lists of `landing-content.ts`.
 */
@Component({
  selector: 'app-portal-landing-page',
  imports: [
    DgaCardComponent,
    DgaHeroComponent,
    DgaHeroSliderComponent,
    DgaIconComponent,
    LandingDashboardPatternComponent,
    RouterLink,
    TranslatePipe,
  ],
  templateUrl: './portal-landing-page.component.html',
  styleUrl: './portal-landing-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PortalLandingPageComponent {
  private readonly translations = inject(TranslationService);

  protected readonly keys = LandingTranslationKeys;
  protected readonly links = PortalLinks;
  protected readonly heroVariant = DgaHeroVariant.Brand;
  protected readonly services = SERVICE_CARDS;
  protected readonly tiles = LANDING_TILES;
  protected readonly quickActions = LANDING_QUICK_ACTIONS;

  readonly slides = computed(() => heroSlides(LANDING_SLIDES, key => this.translations.translate(key)));
}
