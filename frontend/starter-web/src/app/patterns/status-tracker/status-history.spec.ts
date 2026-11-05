import { describe, expect, it } from 'vitest';
import { HistoryOrder, NEUTRAL_STATUS_TONE, formatHistoryInstant, latestEntry, sortHistory, type StatusHistoryEntry } from './status-history';

const SUBMITTED: StatusHistoryEntry = { statusKey: 'requests.status.submitted', at: '2026-03-01T08:00:00Z' };
const IN_REVIEW: StatusHistoryEntry = { statusKey: 'requests.status.in-review', at: '2026-03-02T09:30:00Z', actorLabel: 'Reviewer' };
const RETURNED: StatusHistoryEntry = { statusKey: 'requests.status.returned', at: '2026-03-03T10:00:00Z', reasonText: 'Missing page' };
const BROKEN: StatusHistoryEntry = { statusKey: 'requests.status.unknown', at: 'not-a-date' };

describe('sortHistory', () => {
  it('sorts newest first or oldest first without mutating the input', () => {
    const entries = [IN_REVIEW, SUBMITTED, RETURNED];
    expect(sortHistory(entries, HistoryOrder.NewestFirst)).toEqual([RETURNED, IN_REVIEW, SUBMITTED]);
    expect(sortHistory(entries, HistoryOrder.OldestFirst)).toEqual([SUBMITTED, IN_REVIEW, RETURNED]);
    expect(entries).toEqual([IN_REVIEW, SUBMITTED, RETURNED]);
  });

  it('keeps the API order for equal instants and sorts an unparseable instant as the oldest', () => {
    const twin = { ...SUBMITTED, statusKey: 'requests.status.twin' };
    expect(sortHistory([SUBMITTED, twin], HistoryOrder.NewestFirst)).toEqual([SUBMITTED, twin]);
    expect(sortHistory([BROKEN, SUBMITTED], HistoryOrder.NewestFirst)).toEqual([SUBMITTED, BROKEN]);
  });
});

describe('latestEntry', () => {
  it('picks the latest change, the last one sent on a tie, and null for no history', () => {
    expect(latestEntry([RETURNED, SUBMITTED, IN_REVIEW])).toBe(RETURNED);
    const twin = { ...RETURNED, statusKey: 'requests.status.twin' };
    expect(latestEntry([RETURNED, twin])).toBe(twin);
    expect(latestEntry([])).toBeNull();
  });
});

describe('formatHistoryInstant', () => {
  it('formats in the app language and returns the raw value when it does not parse', () => {
    const english = formatHistoryInstant(SUBMITTED.at, 'en');
    const arabic = formatHistoryInstant(SUBMITTED.at, 'ar');
    expect(english).toContain('2026');
    expect(arabic).not.toBe(english);
    expect(formatHistoryInstant('not-a-date', 'en')).toBe('not-a-date');
  });
});

describe('NEUTRAL_STATUS_TONE', () => {
  it('maps every status to neutral', () => {
    expect(NEUTRAL_STATUS_TONE('requests.status.submitted')).toBe('neutral');
  });
});
