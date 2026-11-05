import { describe, expect, it } from 'vitest';
import { DueState, SortDirection, dueState } from '../../patterns';
import { showcaseClock } from '../showcase-clock';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import {
  ALLOWED_ACTIONS_FIXTURE,
  ORG_FIXTURE,
  PORTAL_SECTIONS,
  QUEUE_FIXTURE,
  QueueStatus,
  QueueView,
  TREE_KEYS,
  TREE_LABELS,
  applyListQuery,
  createdAtFixture,
  historyFixture,
  historyTone,
  kpiTiles,
  queueRows,
  queueTabs,
} from './pattern-fixtures';

const P = ShowcaseTranslationKeys.Patterns;
const translate = (key: string) => `T(${key})`;
const byCode = { textOf: (row: (typeof ORG_FIXTURE)[number]) => row.code, sortValue: (row: (typeof ORG_FIXTURE)[number]) => row.code };

describe('applyListQuery', () => {
  it('keeps the fixture order without search or sort', () => {
    expect(applyListQuery(ORG_FIXTURE, { search: '', sort: null }, byCode)).toBe(ORG_FIXTURE);
  });

  it('filters case-insensitively on the text, ignoring surrounding spaces', () => {
    expect(applyListQuery(ORG_FIXTURE, { search: '  edu ', sort: null }, byCode).map(row => row.id)).toEqual(['org-education']);
  });

  it('sorts ascending and descending on the sort value', () => {
    const ascending = applyListQuery(ORG_FIXTURE, { search: '', sort: { key: 'code', direction: SortDirection.Ascending } }, byCode);
    const descending = applyListQuery(ORG_FIXTURE, { search: '', sort: { key: 'code', direction: SortDirection.Descending } }, byCode);
    expect(ascending.map(row => row.code)).toEqual(['CUL-05', 'EDU-02', 'HLT-01', 'HSG-04', 'TRN-03']);
    expect(descending.map(row => row.code)).toEqual([...ascending.map(row => row.code)].reverse());
  });
});

describe('the tree fixture', () => {
  it('labels every key and every group prefix', () => {
    const prefixes = TREE_KEYS.flatMap(key => key.split('.').map((_part, index, parts) => parts.slice(0, index + 1).join('.')));
    for (const key of new Set(prefixes)) {
      expect(TREE_LABELS[key], key).toBeDefined();
    }
  });
});

describe('the work-queue fixture', () => {
  it('counts the rows of each view on its tab', () => {
    const tabs = queueTabs();
    for (const tab of tabs) {
      expect(tab.count, tab.value).toBe(QUEUE_FIXTURE.filter(row => row.views.some(view => view === tab.value)).length);
    }
  });

  it('narrows a view to one status', () => {
    const returned = queueRows(QueueView.All, QueueStatus.Returned, showcaseClock);
    expect(returned.map(row => row.id)).toEqual(['task-3']);
    expect(queueRows(QueueView.All, null, showcaseClock)).toHaveLength(QUEUE_FIXTURE.length);
  });

  it('resolves due times against the clock: on track, due soon, overdue and none', () => {
    const now = showcaseClock();
    const states = queueRows(QueueView.All, null, showcaseClock).map(row => dueState(row.dueAt, now));
    expect(states).toEqual([DueState.OnTrack, DueState.DueSoon, DueState.Overdue, null]);
  });
});

describe('the history fixture', () => {
  it('runs oldest to newest before now, translated, with the return reason', () => {
    const history = historyFixture(showcaseClock, translate);
    const times = history.map(entry => Date.parse(entry.at));
    expect(times).toEqual([...times].sort((a, b) => a - b));
    expect(times.every(time => time < showcaseClock())).toBe(true);
    expect(history.find(entry => entry.statusKey === P.HistoryReturned)?.reasonText).toBe(`T(${P.HistoryReason})`);
    expect(createdAtFixture(showcaseClock)).toBe(history[0]?.at);
  });

  it('gives each status a tone, and neutral to an unknown one', () => {
    expect(historyTone(P.HistoryReturned)).toBe('warning');
    expect(historyTone('unknown')).toBe('neutral');
  });
});

describe('the other pattern fixtures', () => {
  it('has the three decisions, two of them needing a reason', () => {
    expect(ALLOWED_ACTIONS_FIXTURE.filter(action => action.requiresReason)).toHaveLength(2);
  });

  it('has a table of contents with a subsection', () => {
    expect(PORTAL_SECTIONS.some(section => section.depth !== undefined)).toBe(true);
  });

  it('has number tiles and one translated text tile', () => {
    const tiles = kpiTiles(translate);
    expect(tiles.filter(tile => typeof tile.value === 'string').map(tile => tile.value)).toEqual([`T(${P.KpiAverageValue})`]);
  });
});
