import { describe, expect, it } from 'vitest';
import { echoTranslate } from '../patterns.testing';
import { TreeCheckState } from '../tree-editor/key-tree';
import {
  DEFAULT_DUE_SOON_WINDOW_MS,
  DUE_STATE_DISPLAY,
  DueState,
  dueState,
  dueTime,
  formatDueAt,
  pageSelectionState,
  queueTabsFor,
  setPageSelection,
  toggleSelection,
} from './queue-state';
import { WORK_QUEUE_TRANSLATION_KEYS, WorkQueueTranslationKeys } from './work-queue-translation-keys';

const NOW = Date.parse('2026-03-10T12:00:00Z');
const HOUR = 3_600_000;

describe('dueTime', () => {
  it('reads ISO date-times and rejects missing or unreadable values', () => {
    expect(dueTime('2026-03-10T12:00:00Z')).toBe(NOW);
    expect(dueTime(null)).toBeNull();
    expect(dueTime(undefined)).toBeNull();
    expect(dueTime('')).toBeNull();
    expect(dueTime('not a date')).toBeNull();
  });
});

describe('dueState', () => {
  it('is overdue once the due time is reached', () => {
    expect(dueState('2026-03-10T12:00:00Z', NOW)).toBe(DueState.Overdue);
    expect(dueState('2026-03-09T08:00:00Z', NOW)).toBe(DueState.Overdue);
  });

  it('is due soon inside the window and on track outside it', () => {
    expect(DEFAULT_DUE_SOON_WINDOW_MS).toBe(24 * HOUR);
    expect(dueState('2026-03-11T12:00:00Z', NOW)).toBe(DueState.DueSoon);
    expect(dueState('2026-03-11T12:00:01Z', NOW)).toBe(DueState.OnTrack);
  });

  it('honours a screen window', () => {
    expect(dueState('2026-03-10T15:00:00Z', NOW, 2 * HOUR)).toBe(DueState.OnTrack);
    expect(dueState('2026-03-10T13:00:00Z', NOW, 2 * HOUR)).toBe(DueState.DueSoon);
  });

  it('is null without a readable due time', () => {
    expect(dueState(null, NOW)).toBeNull();
    expect(dueState('soon', NOW)).toBeNull();
  });

  it('maps every state to a work-queue key and a tone', () => {
    expect(DUE_STATE_DISPLAY[DueState.OnTrack].tone).toBe('neutral');
    expect(DUE_STATE_DISPLAY[DueState.DueSoon].tone).toBe('warning');
    expect(DUE_STATE_DISPLAY[DueState.Overdue].tone).toBe('error');
    for (const display of Object.values(DUE_STATE_DISPLAY)) {
      expect(WORK_QUEUE_TRANSLATION_KEYS).toContain(display.labelKey);
    }
  });
});

describe('formatDueAt', () => {
  it('formats in the given language and leaves unreadable values empty', () => {
    expect(formatDueAt('2026-03-10T12:00:00Z', 'en')).toContain('2026');
    expect(formatDueAt('2026-03-10T12:00:00Z', 'ar')).not.toBe('');
    expect(formatDueAt(null, 'en')).toBe('');
  });
});

describe('queueTabsFor', () => {
  it('translates labels and appends known counts', () => {
    const tabs = queueTabsFor(
      [
        { value: 'mine', labelKey: 'queue.mine', count: 4 },
        { value: 'team', labelKey: 'queue.team', count: 0 },
        { value: 'all', labelKey: 'queue.all', disabled: true },
      ],
      echoTranslate,
    );
    expect(tabs).toEqual([
      { value: 'mine', label: `${WorkQueueTranslationKeys.TabCount}|label=queue.mine,count=4`, disabled: false },
      { value: 'team', label: `${WorkQueueTranslationKeys.TabCount}|label=queue.team,count=0`, disabled: false },
      { value: 'all', label: 'queue.all', disabled: true },
    ]);
  });
});

describe('selection', () => {
  it('toggles one row without changing the original set', () => {
    const start = new Set(['a']);
    expect([...toggleSelection(start, 'b')]).toEqual(['a', 'b']);
    expect([...toggleSelection(start, 'a')]).toEqual([]);
    expect([...start]).toEqual(['a']);
  });

  it('reports the page state', () => {
    expect(pageSelectionState(new Set(), ['a', 'b'])).toBe(TreeCheckState.Unchecked);
    expect(pageSelectionState(new Set(['a', 'z']), ['a', 'b'])).toBe(TreeCheckState.Mixed);
    expect(pageSelectionState(new Set(['a', 'b']), ['a', 'b'])).toBe(TreeCheckState.Checked);
    expect(pageSelectionState(new Set(['a']), [])).toBe(TreeCheckState.Unchecked);
  });

  it('selects or clears a page and keeps rows of other pages', () => {
    expect([...setPageSelection(new Set(['z']), ['a', 'b'], true)]).toEqual(['z', 'a', 'b']);
    expect([...setPageSelection(new Set(['z', 'a', 'b']), ['a', 'b'], false)]).toEqual(['z']);
  });
});

describe('WorkQueueTranslationKeys', () => {
  it('has unique keys under patterns.work-queue.*', () => {
    expect(new Set(WORK_QUEUE_TRANSLATION_KEYS).size).toBe(WORK_QUEUE_TRANSLATION_KEYS.length);
    for (const key of WORK_QUEUE_TRANSLATION_KEYS) {
      expect(key).toMatch(/^patterns\.work-queue\.[a-z-]+$/);
    }
  });
});
