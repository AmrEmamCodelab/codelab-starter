import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { TimelineOrder, type ActionLabelResolver } from './record-timeline';
import { RecordTimelineComponent } from './record-timeline.component';
import { RecordsTranslationKeys } from './records-translation-keys';
import { TransitionSide, WorkflowAudience, type TimelineEntry } from './records.models';
import { TEST_STATUS_PREFIX, anEntry, recordsUiTestProviders, translated } from './records.testing';
import { statusWordingResolver } from './status-wording';

@Component({
  selector: 'app-record-timeline-host',
  imports: [RecordTimelineComponent],
  template: `<app-record-timeline [entries]="entries()" [audience]="audience()" [statusKeyFor]="statusKeyFor"
    [actionLabelKeyFor]="actionLabelKeyFor()" [order]="order()" />`,
})
class HostComponent {
  readonly entries = signal<readonly TimelineEntry[]>([]);
  readonly audience = signal(WorkflowAudience.Requester);
  readonly order = signal(TimelineOrder.NewestFirst);
  readonly actionLabelKeyFor = signal<ActionLabelResolver | null>(null);
  readonly statusKeyFor = statusWordingResolver(TEST_STATUS_PREFIX);
}

const submitted = anEntry({ at: '2026-11-01T09:00:00Z', actionKey: 'submit', toStatusKey: 'submitted' });
const returned = anEntry({
  at: '2026-11-02T09:00:00Z',
  actionKey: 'return',
  side: TransitionSide.Staff,
  actorDisplayName: 'Reviewer',
  toStatusKey: 'returned',
  reason: 'Add the invoice',
});
const resubmitted = anEntry({ at: '2026-11-03T09:00:00Z', actionKey: 'resubmit', toStatusKey: 'resubmitted' });

async function render(configure: (host: HostComponent) => void) {
  TestBed.configureTestingModule({ imports: [HostComponent], providers: recordsUiTestProviders() });
  const fixture = TestBed.createComponent(HostComponent);
  configure(fixture.componentInstance);
  fixture.detectChanges();
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const entries = () => Array.from(root.querySelectorAll<HTMLElement>('.record-timeline__entry'));
  return { fixture, root, entries };
}

describe('RecordTimelineComponent', () => {
  it('lists every step newest first by default', async () => {
    const { entries } = await render((host) => host.entries.set([submitted, resubmitted, returned]));
    expect(entries().map((entry) => entry.dataset['action'])).toEqual(['resubmit', 'return', 'submit']);
  });

  it('lists oldest first when asked', async () => {
    const { entries } = await render((host) => {
      host.entries.set([resubmitted, submitted, returned]);
      host.order.set(TimelineOrder.OldestFirst);
    });
    expect(entries().map((entry) => entry.dataset['action'])).toEqual(['submit', 'return', 'resubmit']);
  });

  it('words each status for the audience and shows side, actor, time and reason', async () => {
    const { entries } = await render((host) => {
      host.entries.set([returned]);
      host.audience.set(WorkflowAudience.Admin);
      host.actionLabelKeyFor.set((actionKey) => `testRecords.action.${actionKey}`);
    });
    const entry = entries()[0];
    expect(entry?.querySelector('.record-timeline__status')?.textContent?.trim()).toBe(`${TEST_STATUS_PREFIX}.returned.admin`);
    expect(entry?.querySelector('.record-timeline__side')?.textContent?.trim()).toBe(RecordsTranslationKeys.Timeline.SideStaff);
    expect(entry?.querySelector('.record-timeline__action')?.textContent?.trim()).toBe('testRecords.action.return');
    expect(entry?.querySelector('.record-timeline__actor')?.textContent?.trim())
      .toBe(translated(RecordsTranslationKeys.Timeline.By, { actor: 'Reviewer' }));
    expect(entry?.querySelector('time')?.getAttribute('datetime')).toBe(returned.at);
    expect(entry?.querySelector('time')?.textContent).toMatch(/2026/);
    expect(entry?.querySelector('.record-timeline__reason-text')?.textContent).toBe('Add the invoice');
  });

  it('shows no reason and no action label when there are none', async () => {
    const { entries } = await render((host) => host.entries.set([submitted]));
    expect(entries()[0]?.querySelector('.record-timeline__reason')).toBeNull();
    expect(entries()[0]?.querySelector('.record-timeline__action')).toBeNull();
  });

  it('is read-only: nothing on it is focusable or clickable', async () => {
    const { root } = await render((host) => host.entries.set([submitted, returned]));
    expect(root.querySelectorAll('button, a, [tabindex], input, textarea')).toHaveLength(0);
  });

  it('shows the empty text without history', async () => {
    const { root, entries } = await render(() => undefined);
    expect(entries()).toHaveLength(0);
    expect(root.textContent).toContain(RecordsTranslationKeys.Timeline.Empty);
  });
});
