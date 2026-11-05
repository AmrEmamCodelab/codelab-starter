import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { TranslationService } from '../../core/i18n/translation.service';
import { echoTranslate, textOf } from '../patterns.testing';
import type { KpiTile, QuickAction } from './kpi-tiles';
import { LandingDashboardPatternComponent } from './landing-dashboard-pattern.component';
import { LandingDashboardTranslationKeys } from './landing-dashboard-translation-keys';

const TILES: readonly KpiTile[] = [
  { labelKey: 'home.kpis.open', value: 1250, icon: 'file', link: '/requests' },
  { labelKey: 'home.kpis.rate', value: '98%' },
  { labelKey: 'home.kpis.pending', value: null },
];
const ACTIONS: readonly QuickAction[] = [{ labelKey: 'home.actions.new', link: '/requests/new', icon: 'plus' }];

/** Living documentation: a home page on the landing-dashboard pattern. */
@Component({
  imports: [LandingDashboardPatternComponent],
  template: `
    <div [attr.dir]="dir()">
      <app-landing-dashboard-pattern
        greetingKey="home.greeting"
        [greetingParams]="{ name: 'Sara' }"
        [tiles]="tiles()"
        [quickActions]="actions()"
        [loading]="loading()"
        [recentLabelKey]="recentLabelKey()"
      >
        <div landingHero class="hero"></div>
        <ul landingRecent class="recent"></ul>
      </app-landing-dashboard-pattern>
    </div>
  `,
})
class HomeHost {
  readonly dir = signal<'rtl' | 'ltr'>('rtl');
  readonly tiles = signal<readonly KpiTile[]>(TILES);
  readonly actions = signal<readonly QuickAction[]>(ACTIONS);
  readonly loading = signal(false);
  readonly recentLabelKey = signal<string | null>(LandingDashboardTranslationKeys.Recent);
}

describe('LandingDashboardPatternComponent', () => {
  let fixture: ComponentFixture<HomeHost>;
  let host: HTMLElement;
  let screen: HomeHost;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HomeHost],
      providers: [provideRouter([]), { provide: TranslationService, useValue: { translate: echoTranslate, language: signal('en') } }],
    });
    fixture = TestBed.createComponent(HomeHost);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    screen = fixture.componentInstance;
  });

  const render = () => fixture.detectChanges();
  const tiles = () => [...host.querySelectorAll<HTMLElement>('.landing__tile')];

  it('renders the greeting, the hero slot and the section headings', () => {
    expect(textOf(host, 'h1')).toBe('home.greeting|name=Sara');
    expect(host.querySelector('.landing__hero .hero')).not.toBeNull();
    expect([...host.querySelectorAll('h2')].map((h2) => h2.textContent?.trim())).toEqual([
      LandingDashboardTranslationKeys.Kpis,
      LandingDashboardTranslationKeys.QuickActions,
      LandingDashboardTranslationKeys.Recent,
    ]);
    expect(host.querySelector('.recent')?.closest('section')?.getAttribute('aria-labelledby')).toBeTruthy();
  });

  it('renders one tile per KPI with the formatted value, the label and the icon', () => {
    expect(tiles()).toHaveLength(3);
    expect(textOf(tiles()[0], '.landing__tile-value')).toBe('1,250');
    expect(textOf(tiles()[0], '.landing__tile-label')).toBe('home.kpis.open');
    expect(tiles()[0].querySelector('dga-icon')).not.toBeNull();
    expect(textOf(tiles()[1], '.landing__tile-value')).toBe('98%');
    expect(textOf(tiles()[2], '.landing__tile-value')).toBe(LandingDashboardTranslationKeys.ValueLoading);
  });

  it('makes a tile with a link a keyboard-reachable link, and the others plain', () => {
    const link = tiles()[0];
    expect(link.tagName).toBe('A');
    expect(link.getAttribute('href')).toBe('/requests');
    link.focus();
    expect(document.activeElement).toBe(link);
    expect(tiles()[1].tagName).toBe('DIV');
  });

  it('shows placeholders and marks the row busy while loading', () => {
    screen.loading.set(true);
    render();
    expect(tiles().every((tile) => textOf(tile, '.landing__tile-value') === LandingDashboardTranslationKeys.ValueLoading)).toBe(true);
    expect(host.querySelector('[aria-busy="true"]')).not.toBeNull();
  });

  it('renders quick actions as links and hides the section when there are none', () => {
    const action = host.querySelector('.landing__action') as HTMLAnchorElement;
    expect(action.getAttribute('href')).toBe('/requests/new');
    expect(textOf(action, '.landing__action-label')).toBe('home.actions.new');
    screen.actions.set([]);
    render();
    expect(host.querySelector('.landing__actions')).toBeNull();
  });

  it('hides the KPI row without tiles and the recent section without a heading', () => {
    screen.tiles.set([]);
    screen.recentLabelKey.set(null);
    render();
    expect(host.querySelector('.landing__tiles')).toBeNull();
    expect(host.querySelector('.recent')).toBeNull();
  });

  it('keeps the tile order in LTR (the grid mirrors itself through logical layout)', () => {
    const rtl = tiles().map((tile) => textOf(tile, '.landing__tile-label'));
    screen.dir.set('ltr');
    render();
    expect(tiles().map((tile) => textOf(tile, '.landing__tile-label'))).toEqual(rtl);
    expect(host.querySelector('.landing__action-arrow')?.getAttribute('data-name')).toBe('chevron-end');
  });
});
