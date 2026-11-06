import { TransitionSide } from './records.models';

/**
 * Every translation key the generic record UI renders on its own, under `records.*` in `src/assets/i18n/ar.json` and
 * `en.json`. Status names, action labels and card labels are the record type's keys (inputs), never these.
 */
export const RecordsTranslationKeys = {
  Cards: {
    /** Shown for a field the record has no value for. */
    EmptyValue: 'records.cards.empty-value',
  },
  Timeline: {
    /** Default heading of the timeline. */
    Title: 'records.timeline.title',
    Empty: 'records.timeline.empty',
    /** `{{actor}}`: who took the step. */
    By: 'records.timeline.by',
    Reason: 'records.timeline.reason',
    SideRequester: 'records.timeline.side.requester',
    SideStaff: 'records.timeline.side.staff',
    SideSystem: 'records.timeline.side.system',
  },
  Decision: {
    /** Default heading of the decision panel. */
    Title: 'records.decision.title',
    /** No action is available (terminal record, or nothing the caller may do). */
    None: 'records.decision.none',
    Reason: 'records.decision.reason',
    ReasonRequiredHint: 'records.decision.reason-required-hint',
    ReasonOptionalHint: 'records.decision.reason-optional-hint',
    ReasonRequired: 'records.decision.reason-required',
    /** `{{max}}`. */
    ReasonTooLong: 'records.decision.reason-too-long',
    /** The ng-dga textarea counter: `{count}` and `{max}` (single braces, filled by the textarea). */
    Counter: 'records.decision.counter',
    Cancel: 'records.decision.cancel',
    /** `{{code}}`: the reference code of a failed decision. */
    ReferenceCode: 'records.decision.reference-code',
  },
  Sla: {
    OnTrack: 'records.sla.on-track',
    DueSoon: 'records.sla.due-soon',
    Breached: 'records.sla.breached',
    /** `{{date}}`: the deadline. */
    DueAt: 'records.sla.due-at',
  },
} as const;

/** The timeline's label of each transition side. */
export const TRANSITION_SIDE_KEYS: Readonly<Record<TransitionSide, string>> = {
  [TransitionSide.Requester]: RecordsTranslationKeys.Timeline.SideRequester,
  [TransitionSide.Staff]: RecordsTranslationKeys.Timeline.SideStaff,
  [TransitionSide.System]: RecordsTranslationKeys.Timeline.SideSystem,
};

/** The flat list of every `records.*` key. */
export const RECORDS_TRANSLATION_KEYS: readonly string[] = Object.values(RecordsTranslationKeys).flatMap((group) =>
  Object.values(group));
