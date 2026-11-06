import { describe, expect, it } from 'vitest';
import { RecordsTranslationKeys } from './records-translation-keys';
import { SlaState } from './records.models';
import { slaDisplay } from './sla';

const DUE = '2026-11-09T10:00:00Z';

describe('sla display', () => {
  it.each([
    [SlaState.OnTrack, RecordsTranslationKeys.Sla.OnTrack, 'success'],
    [SlaState.DueSoon, RecordsTranslationKeys.Sla.DueSoon, 'warning'],
    [SlaState.Breached, RecordsTranslationKeys.Sla.Breached, 'error'],
  ])('shows state %s with its label and tone', (state, labelKey, tone) => {
    expect(slaDisplay({ state, dueAt: DUE })).toEqual({ labelKey, tone });
  });

  it('shows nothing without an SLA or for an unknown state', () => {
    expect(slaDisplay(null)).toBeNull();
    expect(slaDisplay(undefined)).toBeNull();
    expect(slaDisplay({ state: 9 as SlaState, dueAt: DUE })).toBeNull();
  });
});
