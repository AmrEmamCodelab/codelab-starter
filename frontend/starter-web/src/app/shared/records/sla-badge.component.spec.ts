import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { RecordsTranslationKeys } from './records-translation-keys';
import { SlaState, type RecordSla } from './records.models';
import { recordsUiTestProviders } from './records.testing';
import { SlaBadgeComponent } from './sla-badge.component';

@Component({
  selector: 'app-sla-badge-host',
  imports: [SlaBadgeComponent],
  template: '<app-sla-badge [sla]="sla()" [compact]="compact()" />',
})
class HostComponent {
  readonly sla = signal<RecordSla | null>(null);
  readonly compact = signal(false);
}

const DUE = '2026-11-09T10:00:00Z';

async function render(sla: RecordSla | null, compact = false): Promise<HTMLElement> {
  TestBed.configureTestingModule({ imports: [HostComponent], providers: recordsUiTestProviders() });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.sla.set(sla);
  fixture.componentInstance.compact.set(compact);
  fixture.detectChanges();
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('SlaBadgeComponent', () => {
  it.each([
    [SlaState.OnTrack, RecordsTranslationKeys.Sla.OnTrack],
    [SlaState.DueSoon, RecordsTranslationKeys.Sla.DueSoon],
    [SlaState.Breached, RecordsTranslationKeys.Sla.Breached],
  ])('shows state %s with its label and the due date', async (state, labelKey) => {
    const root = await render({ state, dueAt: DUE });
    expect(root.querySelector('[data-sla-state]')?.getAttribute('data-sla-state')).toBe(String(state));
    expect(root.querySelector('dga-status-tag')?.textContent?.trim()).toBe(labelKey);
    const due = root.querySelector('time');
    expect(due?.getAttribute('datetime')).toBe(DUE);
    expect(due?.textContent).toContain(RecordsTranslationKeys.Sla.DueAt);
    expect(due?.textContent).toMatch(/2026/);
  });

  it('shows only the tag when compact', async () => {
    const root = await render({ state: SlaState.DueSoon, dueAt: DUE }, true);
    expect(root.querySelector('dga-status-tag')).not.toBeNull();
    expect(root.querySelector('time')).toBeNull();
  });

  it('renders nothing for a record without a deadline or with an unknown state', async () => {
    expect((await render(null)).querySelector('.sla-badge')).toBeNull();
    TestBed.resetTestingModule();
    expect((await render({ state: 7 as SlaState, dueAt: DUE })).querySelector('.sla-badge')).toBeNull();
  });
});
