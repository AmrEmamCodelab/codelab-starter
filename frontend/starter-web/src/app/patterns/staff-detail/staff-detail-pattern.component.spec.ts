import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { parseApiError, type ApiError } from '@codelab/ng-security';
import { beforeEach, describe, expect, it } from 'vitest';
import { patternTestProviders, textOf } from '../patterns.testing';
import { ActionTone, type AllowedAction } from './allowed-actions';
import { StaffDetailPatternComponent } from './staff-detail-pattern.component';
import { StaffDetailTranslationKeys } from './staff-detail-translation-keys';

const ACTIONS: readonly AllowedAction[] = [
  { code: 'approve', labelKey: 'cases.actions.approve', requiresReason: false, tone: ActionTone.Primary },
  { code: 'reject', labelKey: 'cases.actions.reject', requiresReason: true, tone: ActionTone.Destructive },
];

/** Living documentation: a case detail screen on the staff-detail pattern. */
@Component({
  imports: [StaffDetailPatternComponent],
  template: `
    <div [attr.dir]="dir()">
      <app-staff-detail-pattern
        titleKey="cases.detail.title"
        [titleParams]="{ reference: 'C-7' }"
        [actions]="actions()"
        [busy]="busy()"
        [loading]="loading()"
        [error]="error()"
        [historyLabelKey]="historyLabelKey()"
        (decide)="decided.push($event)"
        (retry)="retried = retried + 1"
      >
        <a detailBack class="back" href="#list">back</a>
        <span detailStatus class="status">open</span>
        <span detailHeaderActions class="print"></span>
        <section class="card-applicant"></section>
        <ol detailHistory class="history-list"></ol>
      </app-staff-detail-pattern>
    </div>
  `,
})
class CaseDetailHost {
  readonly dir = signal<'rtl' | 'ltr'>('rtl');
  readonly actions = signal<readonly AllowedAction[]>(ACTIONS);
  readonly busy = signal(false);
  readonly loading = signal(false);
  readonly error = signal<ApiError | null>(null);
  readonly historyLabelKey = signal<string | null>(StaffDetailTranslationKeys.History);
  readonly decided: AllowedAction[] = [];
  retried = 0;
}

describe('StaffDetailPatternComponent', () => {
  let fixture: ComponentFixture<CaseDetailHost>;
  let host: HTMLElement;
  let screen: CaseDetailHost;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [CaseDetailHost], providers: patternTestProviders() });
    fixture = TestBed.createComponent(CaseDetailHost);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    screen = fixture.componentInstance;
  });

  const render = () => fixture.detectChanges();
  const actionButtons = () => [...host.querySelectorAll<HTMLButtonElement>('.staff-detail__actions button')];

  it('renders the header with its slots, the cards, the history and the decision panel', () => {
    expect(textOf(host, 'h1')).toBe('cases.detail.title|reference=C-7');
    expect(host.querySelector('article')?.getAttribute('aria-labelledby')).toBe(host.querySelector('h1')?.id);
    expect(host.querySelector('.staff-detail__back .back')).not.toBeNull();
    expect(host.querySelector('.staff-detail__heading .status')).not.toBeNull();
    expect(host.querySelector('.staff-detail__header-actions .print')).not.toBeNull();
    expect(host.querySelector('.staff-detail__cards .card-applicant')).not.toBeNull();
    expect(host.querySelector('.staff-detail__history .history-list')).not.toBeNull();
    const headings = [...host.querySelectorAll('h2')].map((h2) => h2.textContent?.trim());
    expect(headings).toEqual([StaffDetailTranslationKeys.History, StaffDetailTranslationKeys.Decision]);
  });

  it('renders the allowed actions in panel order, labelled by the panel heading', () => {
    expect(actionButtons().map((button) => button.textContent?.trim())).toEqual(['cases.actions.reject', 'cases.actions.approve']);
    const group = host.querySelector('[role="group"]') as HTMLElement;
    expect(group.getAttribute('aria-labelledby')).toBe(host.querySelector('.staff-detail__decision h2')?.id);
  });

  it('emits decide with the chosen action', () => {
    actionButtons()[1].click();
    actionButtons()[0].click();
    expect(screen.decided.map((action) => action.code)).toEqual(['approve', 'reject']);
  });

  it('keeps every decision a native button in the tab order (Enter and Space work without extra handlers)', () => {
    for (const button of actionButtons()) {
      expect(button.tagName).toBe('BUTTON');
      expect(button.type).toBe('button');
      expect(button.tabIndex).toBe(0);
      button.focus();
      expect(document.activeElement).toBe(button);
    }
  });

  it('disables the decisions while one is in progress', () => {
    screen.busy.set(true);
    render();
    expect(actionButtons().every((button) => button.disabled)).toBe(true);
    actionButtons()[0].click();
    expect(screen.decided).toHaveLength(0);
  });

  it('says so when the server allows no action', () => {
    screen.actions.set([]);
    render();
    expect(actionButtons()).toHaveLength(0);
    expect(textOf(host, '.staff-detail__decision .staff-detail__state')).toBe(StaffDetailTranslationKeys.NoActions);
  });

  it('hides the history section when the screen has none', () => {
    screen.historyLabelKey.set(null);
    render();
    expect(host.querySelector('.staff-detail__history')).toBeNull();
  });

  it('shows the loading state and hides the record while loading', () => {
    screen.loading.set(true);
    render();
    expect(textOf(host, '[role="status"]')).toBe(StaffDetailTranslationKeys.Loading);
    expect((host.querySelector('.staff-detail__layout') as HTMLElement).hidden).toBe(true);
  });

  it('shows the error with its reference code and a retry button instead of the record', () => {
    screen.error.set(parseApiError({ status: 500, error: { code: 'INTERNAL_ERROR', correlationId: '1f2e3d4c5b6a79881726354453627180' } }));
    render();
    const alert = host.querySelector('[role="alert"]') as HTMLElement;
    expect(textOf(alert, '.staff-detail__error-title')).toBe(StaffDetailTranslationKeys.Error);
    expect(textOf(alert, '.staff-detail__error-reference')).toContain('patterns.common.reference-code|code=');
    expect((host.querySelector('.staff-detail__layout') as HTMLElement).hidden).toBe(true);
    (alert.querySelector('button') as HTMLButtonElement).click();
    expect(screen.retried).toBe(1);
  });

  it('keeps the same structure in LTR as in RTL (logical layout, no mirrored markup)', () => {
    const codes = () => [...host.querySelectorAll('dga-button[data-action]')].map((button) => button.getAttribute('data-action'));
    const rtlOrder = codes();
    expect(rtlOrder).toEqual(['reject', 'approve']);
    screen.dir.set('ltr');
    render();
    expect(codes()).toEqual(rtlOrder);
  });
});
