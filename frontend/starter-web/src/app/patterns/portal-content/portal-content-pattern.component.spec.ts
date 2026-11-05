import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { patternTestProviders, textOf } from '../patterns.testing';
import { PortalContentPatternComponent } from './portal-content-pattern.component';
import { PortalContentTranslationKeys } from './portal-content-translation-keys';
import { PortalSectionDirective } from './portal-section.directive';
import { TocDepth, type PortalSection } from './table-of-contents';

const SECTIONS: readonly PortalSection[] = [
  { id: 'about', titleKey: 'guide.about' },
  { id: 'documents', titleKey: 'guide.documents' },
  { id: 'documents-optional', titleKey: 'guide.documents-optional', depth: TocDepth.Subsection },
];

/** Living documentation: a service guide page on the portal-content pattern. */
@Component({
  imports: [PortalContentPatternComponent, PortalSectionDirective],
  template: `
    <div [attr.dir]="dir()">
      <app-portal-content-pattern [titleKey]="titleKey()" subtitleKey="guide.subtitle" [sections]="sections()">
        <div portalHeader class="custom-header"></div>
        <section [portalSection]="'about'"><h2>about</h2></section>
        <section [portalSection]="'documents'"><h2>documents</h2></section>
        <section [portalSection]="'documents-optional'"><h3>optional</h3></section>
        <p portalLastUpdated class="updated">updated</p>
      </app-portal-content-pattern>
    </div>
  `,
})
class GuideHost {
  readonly dir = signal<'rtl' | 'ltr'>('rtl');
  readonly titleKey = signal<string | null>('guide.title');
  readonly sections = signal<readonly PortalSection[]>(SECTIONS);
}

describe('PortalContentPatternComponent', () => {
  let fixture: ComponentFixture<GuideHost>;
  let host: HTMLElement;
  let screen: GuideHost;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [GuideHost], providers: patternTestProviders() });
    fixture = TestBed.createComponent(GuideHost);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    screen = fixture.componentInstance;
    document.body.appendChild(host);
  });

  afterEach(() => {
    host.remove();
    vi.restoreAllMocks();
  });

  const render = () => fixture.detectChanges();
  const links = () => [...host.querySelectorAll<HTMLAnchorElement>('nav a')];

  it('renders the title, the projected header, the sections and the last-updated slot', () => {
    expect(textOf(host, 'h1')).toBe('guide.title');
    expect(textOf(host, '.portal-content__subtitle')).toBe('guide.subtitle');
    expect(host.querySelector('.portal-content__header .custom-header')).not.toBeNull();
    expect(host.querySelectorAll('.portal-content__sections section')).toHaveLength(3);
    expect(host.querySelector('.portal-content__footer .updated')).not.toBeNull();
  });

  it('leaves the heading to the projected header when no title is set', () => {
    screen.titleKey.set(null);
    render();
    expect(host.querySelector('h1')).toBeNull();
  });

  it('gives each marked section its anchor id and script-only focusability', () => {
    const section = host.querySelector('section') as HTMLElement;
    expect(section.id).toBe('section-about');
    expect(section.getAttribute('tabindex')).toBe('-1');
    expect(section.classList).toContain('portal-section');
  });

  it('builds a labelled table of contents with in-page links, depths and the first section current', () => {
    const nav = host.querySelector('nav') as HTMLElement;
    expect(nav.getAttribute('aria-labelledby')).toBe(nav.querySelector('h2')?.id);
    expect(textOf(nav, 'h2')).toBe(PortalContentTranslationKeys.Toc);
    expect(links().map((link) => link.textContent?.trim())).toEqual(['guide.about', 'guide.documents', 'guide.documents-optional']);
    expect(links()[0].getAttribute('href')).toMatch(/#section-about$/);
    expect([...host.querySelectorAll('nav li')].map((item) => item.getAttribute('data-depth'))).toEqual(['1', '1', '2']);
    expect(links()[0].getAttribute('aria-current')).toBe('location');
    expect(links()[1].getAttribute('aria-current')).toBeNull();
  });

  it('follows a link in place: no navigation, scroll to the section, focus on it, entry marked current', () => {
    const target = host.querySelector('#section-documents') as HTMLElement;
    const scroll = vi.fn();
    target.scrollIntoView = scroll;
    const click = new MouseEvent('click', { bubbles: true, cancelable: true });
    links()[1].dispatchEvent(click);
    render();
    expect(click.defaultPrevented).toBe(true);
    expect(scroll).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' });
    expect(document.activeElement).toBe(target);
    expect(links()[1].getAttribute('aria-current')).toBe('location');
    expect(links()[0].getAttribute('aria-current')).toBeNull();
  });

  it('scrolls without animation when the reader prefers reduced motion', () => {
    const original = window.matchMedia;
    window.matchMedia = vi.fn().mockReturnValue({ matches: true } as MediaQueryList);
    const target = host.querySelector('#section-about') as HTMLElement;
    const scroll = vi.fn();
    target.scrollIntoView = scroll;
    links()[0].click();
    expect(scroll).toHaveBeenCalledWith({ behavior: 'auto', block: 'start' });
    window.matchMedia = original;
  });

  it('is operable from the keyboard: links are focusable and activating one moves focus to its section', () => {
    for (const link of links()) {
      link.focus();
      expect(document.activeElement).toBe(link);
    }
    const target = host.querySelector('#section-documents-optional') as HTMLElement;
    links()[2].click();
    expect(document.activeElement).toBe(target);
  });

  it('hides the table of contents for a single section', () => {
    screen.sections.set([SECTIONS[0]]);
    render();
    expect(host.querySelector('nav')).toBeNull();
  });

  it('keeps the same reading order in LTR', () => {
    const rtl = links().map((link) => link.textContent?.trim());
    screen.dir.set('ltr');
    render();
    expect(links().map((link) => link.textContent?.trim())).toEqual(rtl);
  });
});
