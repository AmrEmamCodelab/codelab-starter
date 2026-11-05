import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { provideTestAppConfig } from '../../core/config/app-config.testing';
import {
  ANY_URL_ROUTES,
  BOTH_DIRECTIONS,
  fakeAuth,
  fixedClock,
  layoutTestProviders,
  useDirection,
  type FakeAuth,
} from '../layout.testing';
import { LayoutChromeTranslationKeys as Keys, PortalNavTranslationKeys } from './layout-chrome-translation-keys';
import { PORTAL_MAIN_ID, PortalShellComponent } from './portal-shell.component';

describe.each(BOTH_DIRECTIONS)('PortalShellComponent (%s)', direction => {
  let restoreDirection: () => void;
  let fixture: ComponentFixture<PortalShellComponent>;
  let host: HTMLElement;

  beforeEach(() => {
    restoreDirection = useDirection(direction);
  });

  afterEach(() => {
    host.remove();
    restoreDirection();
  });

  function render(auth: FakeAuth = fakeAuth(null)): void {
    TestBed.configureTestingModule({
      imports: [PortalShellComponent],
      providers: [provideRouter(ANY_URL_ROUTES), ...layoutTestProviders(auth), fixedClock(), provideTestAppConfig()],
    });
    fixture = TestBed.createComponent(PortalShellComponent);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);
  }

  const navLinks = () => [...host.querySelectorAll<HTMLAnchorElement>('.dga-header__nav a.dga-header__link')];

  it('frames the page: stamp strip, header, main outlet and footer, in that order', () => {
    render();
    const parts = [...host.querySelectorAll('dga-stamp-strip, dga-header, main, app-footer')].map(part =>
      part.tagName.toLowerCase(),
    );
    expect(parts).toEqual(['dga-stamp-strip', 'dga-header', 'main', 'app-footer']);
    expect(host.querySelector('main router-outlet')).not.toBeNull();
    expect(host.querySelector('main')?.id).toBe(PORTAL_MAIN_ID);
  });

  it('labels the stamp strip and the header from translations', () => {
    render();
    expect(host.querySelector('dga-stamp-strip')?.textContent).toContain(Keys.StampText);
    expect(host.querySelector('dga-header nav')?.getAttribute('aria-label')).toBe(Keys.NavLabel);
    expect(host.querySelector('.dga-header__menu-button')?.getAttribute('aria-label')).toBe(Keys.MenuLabel);
    expect(host.querySelector('.dga-header__brand-name')?.textContent?.trim()).toBe(Keys.BrandName);
  });

  it('offers the four portal pages in the navigation', () => {
    render();
    expect(navLinks().map(link => link.getAttribute('href'))).toEqual([
      '/portal',
      '/portal/service-guide',
      '/portal/faq',
      '/portal/contact',
    ]);
    expect(navLinks().map(link => link.querySelector(':scope > span')?.textContent?.trim())).toEqual(
      Object.values(PortalNavTranslationKeys),
    );
  });

  it('offers a visitor the language switcher and the sign-in link, not the user menu', () => {
    render();
    expect(host.querySelector('[dgaHeaderActions] app-language-switcher')).not.toBeNull();
    expect(host.querySelector('.frame__sign-in')?.getAttribute('href')).toBe('/login');
    expect(host.querySelector('app-user-profile-menu')).toBeNull();
  });

  it('offers a signed-in user the user menu instead of the sign-in link', () => {
    render(fakeAuth());
    expect(host.querySelector('.frame__sign-in')).toBeNull();
    expect(host.querySelector('app-user-profile-menu')).not.toBeNull();
  });

  it('moves focus onto the page from the skip link', () => {
    render();
    const skip = host.querySelector('.frame__skip') as HTMLAnchorElement;
    expect(skip.textContent?.trim()).toBe(Keys.SkipLink);
    skip.click();
    expect(document.activeElement).toBe(host.querySelector('main'));
  });

  it('collapses the navigation behind the menu button and closes it on Escape', () => {
    render();
    const menuButton = host.querySelector('.dga-header__menu-button') as HTMLButtonElement;
    menuButton.click();
    fixture.detectChanges();
    expect(menuButton.getAttribute('aria-expanded')).toBe('true');
    host.querySelector('dga-header nav')?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(menuButton.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(menuButton);
  });
});
