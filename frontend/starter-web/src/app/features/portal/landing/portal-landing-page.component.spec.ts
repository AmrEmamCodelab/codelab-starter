import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { FAQ_CATEGORIES, faqEntryCount } from '../faq/faq-content';
import { PortalLinks } from '../portal-links';
import { TEST_DIRECTIONS, portalTestProviders } from '../portal.testing';
import { GUIDE_STEPS } from '../service-guide/service-guide-content';
import { LANDING_SLIDES, LANDING_TILES, SERVICE_CARDS, heroSlides } from './landing-content';
import { PortalLandingPageComponent } from './portal-landing-page.component';
import { LandingTranslationKeys as Keys } from './landing-translation-keys';

function render(): HTMLElement {
  TestBed.configureTestingModule({ imports: [PortalLandingPageComponent], providers: portalTestProviders() });
  const fixture = TestBed.createComponent(PortalLandingPageComponent);
  fixture.detectChanges();
  return fixture.nativeElement;
}

describe('landing content', () => {
  it('translates the slides for the slider', () => {
    const slides = heroSlides(LANDING_SLIDES, key => `t(${key})`);
    expect(slides[0]).toEqual({
      id: LANDING_SLIDES[0]?.id,
      title: `t(${Keys.SlideApplyTitle})`,
      subtitle: `t(${Keys.SlideApplySubtitle})`,
      link: PortalLinks.ServiceGuide,
      linkLabel: `t(${Keys.SlideApplyLink})`,
    });
    expect(new Set(slides.map(slide => slide.id)).size).toBe(slides.length);
  });

  it('derives the public figures from the portal content', () => {
    expect(LANDING_TILES.map(tile => tile.value)).toEqual([
      GUIDE_STEPS.length,
      faqEntryCount(FAQ_CATEGORIES),
      Object.keys(AppLanguages).length,
    ]);
  });

  it('links every card to a portal page', () => {
    const links: readonly string[] = Object.values(PortalLinks);
    for (const card of SERVICE_CARDS) {
      expect(links).toContain(card.link);
    }
  });
});

describe('PortalLandingPageComponent', () => {
  afterEach(() => document.documentElement.removeAttribute('dir'));

  it.each(TEST_DIRECTIONS)('renders the hero h1 with its links, the slider, the cards and the tiles (%s)', (direction) => {
    document.documentElement.setAttribute('dir', direction);
    const root = render();

    const headings = root.querySelectorAll('h1');
    expect(headings).toHaveLength(1);
    expect(headings[0]?.textContent?.trim()).toBe(Keys.HeroTitle);
    expect([...root.querySelectorAll('.landing-page__action')].map(link => link.getAttribute('href'))).toEqual([
      PortalLinks.ServiceGuide,
      PortalLinks.Contact,
    ]);
    expect(root.querySelector('dga-hero-slider')?.textContent).toContain(Keys.SlideApplyTitle);
    expect([...root.querySelectorAll('.landing-page__card-link')].map(link => link.getAttribute('href'))).toEqual(
      SERVICE_CARDS.map(card => card.link),
    );
    expect(root.querySelectorAll('.landing__tile')).toHaveLength(LANDING_TILES.length);
    expect(root.textContent).toContain(Keys.KpisTitle);
    expect(root.textContent).toContain(Keys.QuickLinksTitle);
  });
});
