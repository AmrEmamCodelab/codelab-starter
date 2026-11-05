import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { DgaHeaderComponent } from './dga-header.component';
import type { DgaNavItem } from './header-navigation';

const NAV: readonly DgaNavItem[] = [
  { id: 'home', label: 'Home', link: '/home', icon: 'home' },
  {
    id: 'services',
    label: 'Services',
    children: [
      { id: 'one', label: 'One', link: '/services/one' },
      { id: 'two', label: 'Two', link: '/services/two' },
      { id: 'more', label: 'More', children: [{ id: 'three', label: 'Three', link: '/services/three' }] },
    ],
  },
];

@Component({
  imports: [DgaHeaderComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-header brandName="Portal" [logoSrc]="logo()" homeLink="/" [navItems]="nav()" navLabel="Main" menuLabel="Menu">
        <a dgaHeaderStart href="#main">skip</a>
        <button dgaHeaderActions type="button">lang</button>
      </dga-header>
    </div>
    <button type="button" class="outside">outside</button>
  `,
})
class HostComponent {
  readonly dir = signal('ltr');
  readonly logo = signal<string | null>('branding/logo.svg');
  readonly nav = signal<readonly DgaNavItem[]>(NAV);
}

/** jsdom lays out no media queries, so the phone layout applies: keyboard specs open the menu panel first. */
async function render(dir: string, { panelOpen = false } = {}) {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.dir.set(dir);
  document.body.appendChild(fixture.nativeElement);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  const trigger = () => host.querySelector<HTMLButtonElement>('.dga-header__trigger')!;
  const submenu = () => host.querySelector<HTMLElement>('.dga-header__submenu')!;
  const submenuLinks = () => Array.from(submenu().querySelectorAll<HTMLAnchorElement>('a'));
  const menuButton = () => host.querySelector<HTMLButtonElement>('.dga-header__menu-button')!;
  const stable = () => fixture.whenStable();
  if (panelOpen) {
    menuButton().click();
    await stable();
  }
  return { fixture, host, trigger, submenu, submenuLinks, menuButton, stable };
}

function press(element: Element, key: string): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
  element.dispatchEvent(event);
  return event;
}

describe.each(['ltr', 'rtl'])('DgaHeaderComponent (%s)', (dir) => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([{ path: '**', children: [] }])] });
  });

  it('renders the brand link with a decorative logo, the slots and a named navigation', async () => {
    const { host, fixture, stable } = await render(dir);
    const brand = host.querySelector<HTMLAnchorElement>('.dga-header__brand')!;
    expect(brand.getAttribute('href')).toBe('/');
    expect(brand.textContent).toContain('Portal');
    expect(brand.querySelector('img')?.getAttribute('alt')).toBe('');
    expect(host.querySelector('nav')?.getAttribute('aria-label')).toBe('Main');
    expect(host.querySelector('.dga-header__start [dgaHeaderStart]')).not.toBeNull();
    expect(host.querySelector('.dga-header__actions [dgaHeaderActions]')).not.toBeNull();

    fixture.componentInstance.logo.set(null);
    await stable();
    expect(brand.querySelector('img')).toBeNull();
  });

  it('renders leaf entries as router links and parents as collapsed disclosure buttons', async () => {
    const { host, trigger, submenu } = await render(dir);
    const leaf = host.querySelector<HTMLAnchorElement>('a.dga-header__link')!;
    expect(leaf.getAttribute('href')).toBe('/home');
    expect(leaf.querySelector('dga-icon')?.getAttribute('data-name')).toBe('home');
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(trigger().getAttribute('aria-controls')).toBe(submenu().id);
    expect(submenu().hidden).toBe(true);
  });

  it('toggles the submenu on click and renders nested entries as a group', async () => {
    const { trigger, submenu, submenuLinks, stable } = await render(dir);
    trigger().click();
    await stable();
    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(submenu().hidden).toBe(false);
    expect(submenuLinks().map((link) => link.getAttribute('href'))).toEqual(['/services/one', '/services/two', '/services/three']);
    expect(submenu().querySelector('.dga-header__group')?.textContent).toContain('More');
    trigger().click();
    await stable();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
  });

  it.each(['ArrowDown', 'Enter', ' '])('opens with %j and focuses the first link', async (key) => {
    const { trigger, submenuLinks, stable } = await render(dir, { panelOpen: true });
    const event = press(trigger(), key);
    await stable();
    expect(event.defaultPrevented).toBe(true);
    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(document.activeElement).toBe(submenuLinks()[0]);
  });

  it('moves between submenu links with the arrow keys, Home and End, wrapping', async () => {
    const { trigger, submenuLinks, stable } = await render(dir, { panelOpen: true });
    press(trigger(), 'ArrowDown');
    await stable();
    press(submenuLinks()[0], 'ArrowDown');
    expect(document.activeElement).toBe(submenuLinks()[1]);
    press(submenuLinks()[1], 'End');
    expect(document.activeElement).toBe(submenuLinks()[2]);
    press(submenuLinks()[2], 'ArrowDown');
    expect(document.activeElement).toBe(submenuLinks()[0]);
    press(submenuLinks()[0], 'ArrowUp');
    expect(document.activeElement).toBe(submenuLinks()[2]);
    press(submenuLinks()[2], 'Home');
    expect(document.activeElement).toBe(submenuLinks()[0]);
  });

  it('closes on Escape and returns focus to its button', async () => {
    const { trigger, submenuLinks, stable } = await render(dir, { panelOpen: true });
    press(trigger(), 'ArrowDown');
    await stable();
    press(submenuLinks()[1], 'Escape');
    await stable();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(trigger());
  });

  it('closes on a click outside and when focus leaves the entry', async () => {
    const { host, trigger, stable } = await render(dir);
    trigger().click();
    await stable();
    host.querySelector<HTMLButtonElement>('.outside')!.click();
    await stable();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');

    trigger().click();
    await stable();
    const outside = host.querySelector<HTMLButtonElement>('.outside')!;
    trigger().dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: outside }));
    await stable();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
  });

  it('keeps the submenu open when focus moves inside the entry', async () => {
    const { trigger, submenuLinks, stable } = await render(dir);
    trigger().click();
    await stable();
    trigger().dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: submenuLinks()[0] }));
    await stable();
    expect(trigger().getAttribute('aria-expanded')).toBe('true');
  });

  it('collapses the navigation behind a named menu button that Escape closes', async () => {
    const { host, menuButton, stable } = await render(dir);
    const nav = host.querySelector('nav')!;
    expect(menuButton().getAttribute('aria-label')).toBe('Menu');
    expect(menuButton().getAttribute('aria-controls')).toBe(nav.id);
    expect(menuButton().getAttribute('aria-expanded')).toBe('false');

    menuButton().click();
    await stable();
    expect(menuButton().getAttribute('aria-expanded')).toBe('true');
    expect(nav.classList.contains('dga-header__nav--open')).toBe(true);
    expect(menuButton().querySelector('dga-icon')?.getAttribute('data-name')).toBe('close');

    press(host.querySelector('a.dga-header__link')!, 'Escape');
    await stable();
    expect(menuButton().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(menuButton());
  });

  it('closes the mobile panel on Escape while focus is still on the menu button', async () => {
    const { menuButton, stable } = await render(dir);
    menuButton().click();
    await stable();
    menuButton().focus();
    const event = press(menuButton(), 'Escape');
    await stable();
    expect(event.defaultPrevented).toBe(true);
    expect(menuButton().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(menuButton());
  });

  it('lets Escape through on the menu button while the panel is closed', async () => {
    const { menuButton, stable } = await render(dir);
    const event = press(menuButton(), 'Escape');
    await stable();
    expect(event.defaultPrevented).toBe(false);
    expect(menuButton().getAttribute('aria-expanded')).toBe('false');
  });

  it('marks only the entry of the current page when a parent-prefix link sits next to its children', async () => {
    const { fixture, host, stable } = await render(dir);
    fixture.componentInstance.nav.set([
      { id: 'home', label: 'Home', link: '/portal' },
      { id: 'faq', label: 'FAQ', link: '/portal/faq' },
      { id: 'more', label: 'More', children: [{ id: 'terms', label: 'Terms', link: '/portal/terms' }] },
    ]);
    const current = () => Array.from(host.querySelectorAll('[aria-current="page"]')).map((link) => link.getAttribute('href'));

    await TestBed.inject(Router).navigateByUrl('/portal/faq');
    await stable();
    expect(current()).toEqual(['/portal/faq']);

    await TestBed.inject(Router).navigateByUrl('/portal/terms');
    await stable();
    expect(current()).toEqual(['/portal/terms']);

    await TestBed.inject(Router).navigateByUrl('/portal/privacy');
    await stable();
    expect(current()).toEqual([]);

    await TestBed.inject(Router).navigateByUrl('/portal');
    await stable();
    expect(current()).toEqual(['/portal']);
  });

  it('keeps prefix matching for a link no other entry links below', async () => {
    const { host, stable } = await render(dir);
    await TestBed.inject(Router).navigateByUrl('/home/details');
    await stable();
    expect(host.querySelector('a.dga-header__link')?.getAttribute('aria-current')).toBe('page');
  });

  it('closes the mobile panel when a link is followed', async () => {
    const { host, menuButton, stable } = await render(dir);
    menuButton().click();
    await stable();
    host.querySelector<HTMLAnchorElement>('a.dga-header__link')!.click();
    await stable();
    expect(menuButton().getAttribute('aria-expanded')).toBe('false');
  });

  it('renders no navigation or menu button without entries', async () => {
    const { host, fixture, stable } = await render(dir);
    fixture.componentInstance.nav.set([]);
    await stable();
    expect(host.querySelector('nav')).toBeNull();
    expect(host.querySelector('.dga-header__menu-button')).toBeNull();
  });
});
