import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import type { DgaStatusTone } from '@codelab/ng-dga';
import { beforeEach, describe, expect, it } from 'vitest';
import type { AppLanguage } from '../../core/i18n/language';
import { LanguageService } from '../../core/i18n/language.service';
import { patternTestProviders, textOf } from '../patterns.testing';
import { HistoryOrder, formatHistoryInstant, type StatusHistoryEntry } from './status-history';
import { StatusTrackerComponent, StatusTrackerHeadingLevel } from './status-tracker.component';
import { StatusTrackerTranslationKeys } from './status-tracker-translation-keys';

const HISTORY: readonly StatusHistoryEntry[] = [
  { statusKey: 'requests.status.submitted', at: '2026-03-01T08:00:00Z', actorLabel: 'Applicant' },
  { statusKey: 'requests.status.returned', at: '2026-03-03T10:00:00Z', actorLabel: 'Reviewer', reasonText: 'Missing page' },
  { statusKey: 'requests.status.in-review', at: '2026-03-02T09:30:00Z' },
];

const TONES: Readonly<Record<string, DgaStatusTone>> = { 'requests.status.returned': 'warning' };

/** Living documentation: how a screen uses the status tracker. */
@Component({
  imports: [StatusTrackerComponent],
  template: `
    <div [attr.dir]="dir()">
      <app-status-tracker [entries]="entries()" [toneFor]="toneFor" [order]="order()" [current]="current()" [headingLevel]="level()" />
    </div>
  `,
})
class TrackerHost {
  readonly entries = signal<readonly StatusHistoryEntry[]>(HISTORY);
  readonly order = signal<HistoryOrder>(HistoryOrder.NewestFirst);
  readonly current = signal<StatusHistoryEntry | null>(null);
  readonly level = signal<StatusTrackerHeadingLevel>(StatusTrackerHeadingLevel.Page);
  readonly dir = signal('ltr');
  readonly toneFor = (statusKey: string): DgaStatusTone => TONES[statusKey] ?? 'neutral';
}

describe('StatusTrackerComponent', () => {
  let fixture: ComponentFixture<TrackerHost>;
  let host: HTMLElement;
  let screen: TrackerHost;
  const language = signal<AppLanguage>('en');

  beforeEach(async () => {
    language.set('en');
    TestBed.configureTestingModule({
      imports: [TrackerHost],
      providers: [...patternTestProviders(), { provide: LanguageService, useValue: { current: language } }],
    });
    fixture = TestBed.createComponent(TrackerHost);
    host = fixture.nativeElement as HTMLElement;
    screen = fixture.componentInstance;
    await fixture.whenStable();
  });

  const statuses = () => [...host.querySelectorAll('.status-tracker__status')].map((status) => status.textContent?.trim());

  it('names the region by its heading and shows the history title', () => {
    const heading = host.querySelector('.status-tracker__title');
    expect(heading?.getAttribute('role')).toBe('heading');
    expect(heading?.getAttribute('aria-level')).toBe('2');
    expect(host.querySelector('section')?.getAttribute('aria-labelledby')).toBe(heading?.id);
    expect(textOf(host, '.status-tracker__title')).toBe(StatusTrackerTranslationKeys.Title);
    expect(host.querySelector('.status-tracker__history-title')?.getAttribute('aria-level')).toBe('3');
  });

  it('shows the latest change as the current status with its tone, date and reason', () => {
    const current = host.querySelector('.status-tracker__current') as HTMLElement;
    expect(textOf(current, 'dga-status-tag')).toBe('requests.status.returned');
    expect(current.querySelector('.dga-status')?.getAttribute('data-tone')).toBe('warning');
    const since = current.querySelector('time');
    expect(since?.getAttribute('datetime')).toBe('2026-03-03T10:00:00Z');
    expect(since?.textContent?.trim()).toBe(
      `${StatusTrackerTranslationKeys.Since}|date=${formatHistoryInstant('2026-03-03T10:00:00Z', 'en')}`,
    );
    expect(textOf(current, '.status-tracker__current-reason span:last-child')).toBe('Missing page');
  });

  it('lists the history newest first by default, marking the current entry, with actor and reason', () => {
    expect(statuses()).toEqual(['requests.status.returned', 'requests.status.in-review', 'requests.status.submitted']);
    const entries = [...host.querySelectorAll<HTMLElement>('.status-tracker__entry')];
    expect(entries.map((entry) => entry.getAttribute('aria-current'))).toEqual(['true', null, null]);
    expect(textOf(entries[0], '.status-tracker__actor')).toBe(`${StatusTrackerTranslationKeys.By}|actor=Reviewer`);
    expect(entries[1].querySelector('.status-tracker__actor')).toBeNull();
    expect(entries.every((entry) => entry.querySelector('.status-tracker__marker')?.getAttribute('aria-hidden') === 'true')).toBe(true);
  });

  it('lists oldest first on request', async () => {
    screen.order.set(HistoryOrder.OldestFirst);
    await fixture.whenStable();
    expect(statuses()).toEqual(['requests.status.submitted', 'requests.status.in-review', 'requests.status.returned']);
  });

  it('takes the current status from the screen when given', async () => {
    screen.current.set({ statusKey: 'requests.status.closed', at: '2026-03-04T10:00:00Z' });
    await fixture.whenStable();
    expect(textOf(host, '.status-tracker__current dga-status-tag')).toBe('requests.status.closed');
    expect(host.querySelector('.status-tracker__entry[aria-current]')).toBeNull();
  });

  it('shows the empty state and no current card without history', async () => {
    screen.entries.set([]);
    await fixture.whenStable();
    expect(textOf(host, '.status-tracker__empty')).toBe(StatusTrackerTranslationKeys.Empty);
    expect(host.querySelector('.status-tracker__current')).toBeNull();
    expect(host.querySelector('ol')).toBeNull();
  });

  it('is read-only: nothing in it takes keyboard focus', () => {
    expect(host.querySelectorAll('button, a[href], input, select, textarea, [tabindex]')).toHaveLength(0);
  });

  it('formats dates in the app language and keeps the same structure in RTL', async () => {
    screen.dir.set('rtl');
    language.set('ar');
    await fixture.whenStable();
    const firstAt = host.querySelector('.status-tracker__at')?.textContent?.trim();
    expect(firstAt).toBe(formatHistoryInstant('2026-03-03T10:00:00Z', 'ar'));
    expect(statuses()).toEqual(['requests.status.returned', 'requests.status.in-review', 'requests.status.submitted']);
  });

  it('follows the page outline with a lower heading level inside a section', async () => {
    screen.level.set(StatusTrackerHeadingLevel.Section);
    await fixture.whenStable();
    expect(host.querySelector('.status-tracker__title')?.getAttribute('aria-level')).toBe('3');
    expect(host.querySelector('.status-tracker__history-title')?.getAttribute('aria-level')).toBe('4');
  });
});
