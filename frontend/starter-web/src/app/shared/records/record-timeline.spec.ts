import { describe, expect, it } from 'vitest';
import { TimelineOrder, sortTimeline, timelineRows } from './record-timeline';
import { RecordsTranslationKeys } from './records-translation-keys';
import { TransitionSide, WorkflowAudience } from './records.models';
import { anEntry } from './records.testing';
import { statusWordingResolver } from './status-wording';

const created = anEntry({ at: '2026-11-01T08:00:00Z', actionKey: 'create', fromStatus: null, toStatus: 1, toStatusKey: 'draft' });
const submitted = anEntry({ at: '2026-11-01T09:00:00Z', actionKey: 'submit', toStatusKey: 'submitted' });
const rejected = anEntry({
  at: '2026-11-03T12:00:00Z',
  actionKey: 'reject',
  side: TransitionSide.Staff,
  actorDisplayName: 'Reviewer',
  fromStatus: 3,
  toStatus: 7,
  toStatusKey: 'rejected',
  reason: '  Missing document  ',
});

describe('record timeline', () => {
  it('lists the newest step first by default, and oldest first on request', () => {
    const entries = [submitted, rejected, created];
    expect(sortTimeline(entries).map((entry) => entry.actionKey)).toEqual(['reject', 'submit', 'create']);
    expect(sortTimeline(entries, TimelineOrder.OldestFirst).map((entry) => entry.actionKey)).toEqual(['create', 'submit', 'reject']);
  });

  it('keeps the API order for steps at the same instant and never mutates the input', () => {
    const first = anEntry({ actionKey: 'first' });
    const second = anEntry({ actionKey: 'second' });
    const entries = [first, second];
    expect(sortTimeline(entries).map((entry) => entry.actionKey)).toEqual(['first', 'second']);
    expect(sortTimeline(entries, TimelineOrder.OldestFirst).map((entry) => entry.actionKey)).toEqual(['first', 'second']);
    expect(entries).toEqual([first, second]);
  });

  it('sorts an unreadable instant as the oldest step', () => {
    const broken = anEntry({ at: 'garbage', actionKey: 'broken' });
    expect(sortTimeline([broken, submitted]).map((entry) => entry.actionKey)).toEqual(['submit', 'broken']);
  });

  it('renders each step in the audience wording with side, action, actor, time and the trimmed reason', () => {
    const rows = timelineRows([submitted, rejected], {
      audience: WorkflowAudience.Staff,
      statusKeyFor: statusWordingResolver('testRecords.status'),
      actionLabelKeyFor: (actionKey) => `testRecords.action.${actionKey}`,
      order: TimelineOrder.NewestFirst,
      formatAt: (iso) => `at:${iso}`,
    });
    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({
      statusLabelKey: 'testRecords.status.rejected.staff',
      sideLabelKey: RecordsTranslationKeys.Timeline.SideStaff,
      actionLabelKey: 'testRecords.action.reject',
      atText: `at:${rejected.at}`,
      reason: 'Missing document',
    });
    expect(rows[1]).toMatchObject({ sideLabelKey: RecordsTranslationKeys.Timeline.SideRequester, reason: null });
  });

  it('shows no action label without a resolver and no side label for an unknown side', () => {
    const rows = timelineRows([anEntry({ side: 9 as TransitionSide, reason: '   ' })], {
      audience: WorkflowAudience.Requester,
      statusKeyFor: statusWordingResolver('p'),
      actionLabelKeyFor: null,
      order: TimelineOrder.NewestFirst,
      formatAt: (iso) => iso,
    });
    expect(rows[0]).toMatchObject({ actionLabelKey: null, sideLabelKey: null, reason: null, statusLabelKey: 'p.submitted.requester' });
  });
});
