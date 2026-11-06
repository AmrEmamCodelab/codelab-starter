# Generic record UI (`src/app/shared/records`)

## Purpose

The building blocks every record type's screens are made of, with no record type inside: typed clients over the
generic Records and Queues endpoints, and the presentational pieces of a record page (detail cards, timeline,
decision panel, SLA badge, status wording). A record type (the sample `service-request`, or a fork's own) adds only
an adapter (`<type>CardsAdapter(details) -> RecordCard[]`), its wording prefix and its action tones.

| Piece | What it gives a screen | Files |
| --- | --- | --- |
| `RecordsClient` | `mine(type, {status, page, pageSize})`, `detail(type, id)`, `timeline(type, id)`, `transition(type, id, {action, reason})` (blank reason left out; answers the new `RecordDetail`), `oversight(type, {status, number, page, pageSize})`. All quiet (no error dialog) | `records.client.ts` |
| `QueuesClient` | `list(type, {view, status, number, submittedFrom, submittedTo, page, pageSize})`, `claim`, `release`, `assign(type, id, userId)`. All quiet | `queues.client.ts` |
| Paths and params | `RecordsApiPaths`, `QueuesApiPaths` (each value one encoded segment), `RecordsQueryParams`; `queryParams` leaves out null/undefined/blank | `records-api.ts`, `record-query.ts` |
| Models | Hand-written wire shapes of the contract: `RecordId` (`WireId<string>`), `RecordUserId`, `RecordListItem`, `OversightListItem`, `RecordDetail`, `RecordAllowedAction`, `TimelineEntry`, `WorkQueueItem`, `RecordPage<T>`, `QueueView`; int enums `WorkflowAudience`, `ReasonRequirement`, `TransitionSide`, `SlaState`; `WORKFLOW_MAX_REASON_LENGTH` (1000) | `records.models.ts` |
| Errors | `WorkflowErrorCodes`, `RecordsErrorCodes`, `AssignmentsErrorCodes`, `RECORDS_TRANSLATED_CODES` (for `apiErrorMessageKey` / pattern `translatedCodes`), `recordsApiErrorFrom(error)` | `records-error.ts` |
| `statusWording` pipe | `{{ detail.wording \| statusWording: audience \| t }}`: the audience's wording KEY; `statusWordingKey`, `statusWordingResolver(prefix)` (`<prefix>.<statusKey>.<requester\|staff\|admin>`) for the timeline | `status-wording.ts`, `status-wording.pipe.ts` |
| `app-record-detail-cards` | One `dga-card` per `RecordCard {titleKey, fields: {labelKey, value, valueDir?, wide?}[]}`; empty value shows `records.cards.empty-value`; a card without fields is skipped. Inputs `cards`, `headingLevel` | `record-cards.ts`, `record-detail-cards.component.*` |
| `app-record-timeline` | History, newest first by default (`TimelineOrder`), each step in the audience's wording with side tag, optional action label, time, actor and reason; read-only. Inputs `entries`, `audience`, `statusKeyFor`, `actionLabelKeyFor`, `order`, `titleKey`, `headingLevel` | `record-timeline.ts`, `record-timeline.component.*` |
| `app-decision-panel` | One button per server-computed allowed action (nothing for a terminal record); Optional/Required reasons open a `dga-confirm-dialog` with a `dga-textarea`; a blank Required reason and a reason over the limit are blocked. Inputs `allowedActions`, `busy`, `terminal`, `error`, `toneFor`, `maxReasonLength`, `titleKey`, `headingLevel`, `translatedCodes`; output `decide {actionKey, reason}` | `decision.ts`, `decision-panel.component.*` |
| `app-sla-badge` | On track / due soon / overdue `dga-status-tag` plus the due date (`compact` hides the date). Input `sla` | `sla.ts`, `sla-badge.component.*` |
| Translation keys | `RecordsTranslationKeys` (everything under `records.*`), `TRANSITION_SIDE_KEYS`, `RECORDS_TRANSLATION_KEYS` | `records-translation-keys.ts` |
| Spec fixtures | `test-record` (prefix TR) ids, `aRecordDetail`, `anAction`, `aRequiredReasonAction`, `anOptionalReasonAction`, `anEntry`, `recordsUiTestProviders`, `recordsHttpTestProviders` | `records.testing.ts` |

Import from the barrel: `import { RecordsClient, DecisionPanelComponent } from '../../../shared/records';`.

## Use

```ts
protected readonly audience = WorkflowAudience.Staff;
protected readonly statusKeyFor = statusWordingResolver('things.status');
protected readonly toneFor: DecisionToneResolver = (key) => THING_ACTION_TONES[key] ?? DecisionTone.Neutral;

decide(decision: RecordDecision): void {
  this.busy.set(true);                 // synchronously, in the handler: the dialog waits for busy to fall again
  this.error.set(null);
  this.records.transition(THING_RECORD_TYPE, this.id, { action: decision.actionKey, reason: decision.reason }).subscribe({
    next: (detail) => { this.detail.set(detail); this.busy.set(false); },
    error: (failure: unknown) => { this.error.set(recordsApiErrorFrom(failure)); this.busy.set(false); },
  });
}
```

```html
<dga-status-tag>{{ detail().wording | statusWording: audience | t }}</dga-status-tag>
<app-sla-badge [sla]="detail().sla" />
<app-record-detail-cards [cards]="cards()" />
<app-record-timeline [entries]="timeline()" [audience]="audience" [statusKeyFor]="statusKeyFor" />
<app-decision-panel [allowedActions]="detail().allowedActions" [terminal]="detail().isTerminal"
  [busy]="busy()" [error]="error()" [toneFor]="toneFor" (decide)="decide($event)" />
```

## Extend

- A new generic piece (any record type) goes here with a pure helper file and a spec for each; a record-type piece
  belongs in its feature (`features/<type>/`).
- New texts go in `RecordsTranslationKeys` under `records.*` in both `ar.json` and `en.json`
  (`records-translation-keys.spec.ts` checks both and refuses unknown `records.*` keys).
- When `npm run api:gen` emits the Records/Queues schemas, the models may alias the generated types; keep the
  exported names so the screens do not change.

## Rules

- Imports only `core` and `@codelab/ng-dga` (ESLint boundaries: shared -> core). Never a feature, a layout or a
  pattern: that is why the decision panel builds its reason dialog from `dga-confirm-dialog` and `dga-textarea`
  instead of `patterns/decision-dialog` (which also always requires a reason, so it cannot express Optional).
- HTTP only through `ApiService`; every call is quiet, the screen shows the failure.
- No sample vocabulary: record types, status keys, action keys and wording prefixes are inputs.
- The decision panel renders ONLY the server's `allowedActions`; it never adds, infers or filters actions by status.
  Its buttons carry `data-public-action` because that list is the permission gate.
- Enums are integers with explicit values; tokens and logical CSS only.

## Gotchas

- Set `busy` synchronously in the `decide` handler. The reason dialog closes when it sees `busy` false with no
  `error` after it emitted; a screen that starts its call later would see the dialog close at once. Clear `error`
  before each attempt, or a stale one keeps the dialog open.
- An action with `ReasonRequirement.None` emits at once, with no confirmation (like the staff-detail pattern).
- The `statusWording` pipe returns a KEY; pipe it through `t`.
- The textarea counter text uses ng-dga's single-brace `{count}` / `{max}`, not `{{ }}`.
- `RecordsClient.transition` trims the reason and leaves a blank one out of the body.
