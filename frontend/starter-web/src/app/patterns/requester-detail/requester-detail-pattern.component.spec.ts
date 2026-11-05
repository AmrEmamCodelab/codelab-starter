import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { DgaButtonComponent, type DgaStatusTone } from '@codelab/ng-dga';
import type { ApiError } from '@codelab/ng-security';
import { beforeEach, describe, expect, it } from 'vitest';
import type { AppLanguage } from '../../core/i18n/language';
import { LanguageService } from '../../core/i18n/language.service';
import { patternTestProviders, textOf } from '../patterns.testing';
import { formatHistoryInstant, type StatusHistoryEntry } from '../status-tracker/status-history';
import { RequesterDetailPatternComponent } from './requester-detail-pattern.component';
import { RequesterDetailActionsDirective, RequesterDetailAttachmentsDirective } from './requester-detail-slots.directive';
import { RequesterDetailTranslationKeys } from './requester-detail-translation-keys';

const SERVICE_UNAVAILABLE = 503;
const CREATED_AT = '2026-03-01T08:00:00Z';
const HISTORY: readonly StatusHistoryEntry[] = [
  { statusKey: 'requests.status.submitted', at: CREATED_AT },
  { statusKey: 'requests.status.in-review', at: '2026-03-02T09:30:00Z', actorLabel: 'Reviewer' },
];

/** Living documentation: how a requester screen uses the detail pattern, its slots and its gated actions. */
@Component({
  imports: [RequesterDetailPatternComponent, RequesterDetailActionsDirective, RequesterDetailAttachmentsDirective, DgaButtonComponent],
  template: `
    <div [attr.dir]="dir()">
      <app-requester-detail-pattern
        titleKey="requests.detail.title"
        recordNumber="REQ-2026-0001"
        statusKey="requests.status.in-review"
        [statusTone]="tone"
        [createdAt]="createdAt"
        [history]="history"
        [loading]="loading()"
        [error]="error()"
        (retry)="retries = retries + 1"
      >
        @if (banner()) {
          <p detailBanner class="banner">returned</p>
        }
        <div detailSummary class="summary-card">summary</div>
        @if (withAttachments()) {
          <div detailAttachments class="attachments">files</div>
        }
        @if (withActions()) {
          <div detailActions>
            <dga-button class="cancel" variant="secondary-outline" [disabled]="!canCancel()" (click)="cancels = cancels + 1">cancel</dga-button>
          </div>
        }
      </app-requester-detail-pattern>
    </div>
  `,
})
class DetailHost {
  readonly tone: DgaStatusTone = 'progress';
  readonly createdAt = CREATED_AT;
  readonly history = HISTORY;
  readonly loading = signal(false);
  readonly error = signal<ApiError | null>(null);
  readonly banner = signal(false);
  readonly withAttachments = signal(true);
  readonly withActions = signal(true);
  readonly canCancel = signal(true);
  readonly dir = signal('ltr');
  retries = 0;
  cancels = 0;
}

describe('RequesterDetailPatternComponent', () => {
  let fixture: ComponentFixture<DetailHost>;
  let host: HTMLElement;
  let screen: DetailHost;
  const language = signal<AppLanguage>('en');

  async function create(setup?: (detail: DetailHost) => void): Promise<void> {
    fixture = TestBed.createComponent(DetailHost);
    host = fixture.nativeElement as HTMLElement;
    screen = fixture.componentInstance;
    setup?.(screen);
    await fixture.whenStable();
  }

  beforeEach(() => {
    language.set('en');
    TestBed.configureTestingModule({
      imports: [DetailHost],
      providers: [...patternTestProviders(), { provide: LanguageService, useValue: { current: language } }],
    });
  });

  it('shows the header: title, record number, created on and the status tag', async () => {
    await create();
    expect(textOf(host, 'h1')).toBe('requests.detail.title');
    expect(host.querySelector('article')?.getAttribute('aria-labelledby')).toBe(host.querySelector('h1')?.id);
    expect(textOf(host, '.requester-detail__record-number')).toBe('REQ-2026-0001');
    expect(host.querySelector('.requester-detail__meta time')?.textContent).toBe(formatHistoryInstant(CREATED_AT, 'en'));
    expect(textOf(host, '.requester-detail__status dga-status-tag')).toBe('requests.status.in-review');
    expect(host.querySelector('.requester-detail__status .dga-status')?.getAttribute('data-tone')).toBe('progress');
    expect(textOf(host, '.requester-detail__status .dga-visually-hidden')).toBe(RequesterDetailTranslationKeys.Status);
  });

  it('projects the summary cards, attachments section and the status tracker with the history', async () => {
    await create();
    expect(textOf(host, '.requester-detail__summary .summary-card')).toBe('summary');
    expect(textOf(host, '.requester-detail__attachments h2')).toBe(RequesterDetailTranslationKeys.Attachments);
    expect(textOf(host, '.requester-detail__attachments .attachments')).toBe('files');
    expect(host.querySelectorAll('app-status-tracker .status-tracker__entry')).toHaveLength(HISTORY.length);
  });

  it('renders no empty attachments or actions section when the screen projects none', async () => {
    await create((detail) => {
      detail.withAttachments.set(false);
      detail.withActions.set(false);
    });
    expect(host.querySelector('.requester-detail__attachments')).toBeNull();
    expect(host.querySelector('.requester-detail__actions')).toBeNull();
  });

  it('groups the screen actions under a label; the screen decides presence and enablement', async () => {
    await create();
    const group = host.querySelector('.requester-detail__actions');
    expect(group?.getAttribute('role')).toBe('group');
    expect(group?.getAttribute('aria-label')).toBe(RequesterDetailTranslationKeys.Actions);
    const cancel = host.querySelector<HTMLButtonElement>('.cancel button') as HTMLButtonElement;
    cancel.click();
    expect(screen.cancels).toBe(1);
    screen.canCancel.set(false);
    await fixture.whenStable();
    expect(cancel.disabled).toBe(true);
  });

  it('shows the banner slot above the layout', async () => {
    await create((detail) => detail.banner.set(true));
    expect(textOf(host, '.banner')).toBe('returned');
  });

  it('replaces the body with a loading state', async () => {
    await create((detail) => detail.loading.set(true));
    expect(textOf(host, '.requester-detail__loading')).toBe(RequesterDetailTranslationKeys.Loading);
    expect(host.querySelector('article')?.getAttribute('aria-busy')).toBe('true');
    expect(host.querySelector('.requester-detail__layout')).toBeNull();
    expect(host.querySelector('.requester-detail__status')).toBeNull();
  });

  it('shows a load error with its reference code, and Retry (reachable by keyboard) emits retry', async () => {
    await create((detail) =>
      detail.error.set({ status: SERVICE_UNAVAILABLE, code: 'SERVICE_UNAVAILABLE', correlationId: 't', referenceCode: 'REF-7', fieldErrors: [] }),
    );
    const alert = host.querySelector('.requester-detail__error');
    expect(alert?.getAttribute('role')).toBe('alert');
    expect(textOf(host, '.requester-detail__error-title')).toBe(RequesterDetailTranslationKeys.Error);
    expect(textOf(host, '.requester-detail__error-reference')).toBe('patterns.common.reference-code|code=REF-7');
    const retry = host.querySelector<HTMLButtonElement>('.requester-detail__retry button') as HTMLButtonElement;
    expect(retry.tabIndex).toBe(0);
    retry.click();
    expect(screen.retries).toBe(1);
  });

  it('formats the created date in the app language and keeps the layout order in RTL', async () => {
    await create((detail) => detail.dir.set('rtl'));
    language.set('ar');
    await fixture.whenStable();
    expect(host.querySelector('.requester-detail__meta time')?.textContent).toBe(formatHistoryInstant(CREATED_AT, 'ar'));
    const layout = host.querySelector('.requester-detail__layout') as HTMLElement;
    expect([...layout.children].map((child) => child.className)).toEqual(['requester-detail__main', 'requester-detail__aside']);
  });
});
