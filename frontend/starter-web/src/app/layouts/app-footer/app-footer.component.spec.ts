import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { provideTestAppConfig, TEST_APP_CONFIG } from '../../core/config/app-config.testing';
import {
  BOTH_DIRECTIONS,
  fakeAuth,
  fixedClock,
  layoutTestProviders,
  TEST_NOW,
  useDirection,
} from '../layout.testing';
import { AppFooterComponent } from './app-footer.component';
import { copyrightYear, FOOTER_LINKS } from './footer-links';
import { FooterTranslationKeys as Keys } from './footer-translation-keys';

/** 2026-12-31T22:00:00Z is already 2027-01-01 in Saudi time (UTC+3). */
const NEW_YEAR_IN_RIYADH = Date.UTC(2026, 11, 31, 22, 0);

describe('copyrightYear', () => {
  it('is the year of the clock', () => {
    expect(copyrightYear(TEST_NOW)).toBe(2026);
  });

  it('counts the year in Saudi time', () => {
    expect(copyrightYear(NEW_YEAR_IN_RIYADH)).toBe(2027);
  });
});

describe('FOOTER_LINKS', () => {
  it('links the portal privacy, terms and contact pages', () => {
    expect(FOOTER_LINKS.map(link => link.link)).toEqual(['/portal/privacy', '/portal/terms', '/portal/contact']);
  });
});

describe.each(BOTH_DIRECTIONS)('AppFooterComponent (%s)', direction => {
  let restoreDirection: () => void;

  beforeEach(() => {
    restoreDirection = useDirection(direction);
  });

  afterEach(() => restoreDirection());

  function render(logoSrc: string): HTMLElement {
    TestBed.configureTestingModule({
      imports: [AppFooterComponent],
      providers: [
        provideRouter([]),
        ...layoutTestProviders(fakeAuth()),
        fixedClock(),
        provideTestAppConfig({
          ...TEST_APP_CONFIG,
          branding: { primaryColor: '#1b8354', logoSrc, appName: { ar: 'بوابة', en: 'Portal' } },
        }),
      ],
    });
    const fixture = TestBed.createComponent(AppFooterComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the branded name and a decorative logo', () => {
    const host = render('branding/logo.svg');
    expect(host.querySelector('.footer__name')?.textContent?.trim()).toBe('بوابة');
    expect(host.querySelector('.footer__logo')?.getAttribute('src')).toBe('branding/logo.svg');
    expect(host.querySelector('.footer__logo')?.getAttribute('alt')).toBe('');
  });

  it('shows no logo when the branding has none', () => {
    expect(render('').querySelector('.footer__logo')).toBeNull();
  });

  it('renders a labelled list of the portal links', () => {
    const host = render('');
    expect(host.querySelector('footer nav')?.getAttribute('aria-label')).toBe(Keys.LinksLabel);
    const links = [...host.querySelectorAll('.footer__link')];
    expect(links.map(link => link.getAttribute('href'))).toEqual(FOOTER_LINKS.map(link => link.link));
    expect(links.map(link => link.textContent?.trim())).toEqual([Keys.Privacy, Keys.Terms, Keys.Contact]);
  });

  it('prints the copyright year from the injected clock', () => {
    expect(render('').querySelector('.footer__rights')?.textContent?.trim()).toBe(`${Keys.Rights}|year=2026,name=بوابة`);
  });
});
