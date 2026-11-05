# Pattern: requester detail + status tracker

**Use it for** the page a requester sees for one of their own records: what they submitted, where it stands, what
happened to it and when, its attachments, and the few actions open to them (cancel, resubmit after a return).

**Do not use it for** staff deciding on someone else's record (use the [staff detail + decision dialog](staff-detail.md))
or for editing the request (use the [wizard](wizard.md); a resubmission opens the wizard pre-filled).

| Part | Selector / export | Code |
| --- | --- | --- |
| Detail page | `app-requester-detail-pattern` | `src/app/patterns/requester-detail/` |
| Slot markers | `[detailAttachments]`, `[detailActions]` (plus plain `[detailBanner]`, `[detailSummary]`) | `requester-detail/requester-detail-slots.directive.ts` |
| View state | `DetailViewState`, `detailViewState` | `requester-detail/requester-detail-state.ts` |
| Status tracker | `app-status-tracker` (usable alone, for example on a staff page) | `src/app/patterns/status-tracker/` |
| History helpers | `StatusHistoryEntry`, `HistoryOrder`, `sortHistory`, `latestEntry`, `formatHistoryInstant` | `status-tracker/status-history.ts` |

API reference: [patterns/MODULE.md](../../../frontend/starter-web/src/app/patterns/MODULE.md).

## Anatomy

```
┌────────────────────────────────────────────────────────────────────────┐
│ Request details                                          (● In review)  │  h1 titleKey + status tag (statusTone)
│ Request number REQ-2026-0001   Created on 1 Mar 2026, 11:00             │  recordNumber, createdAt (app language)
├────────────────────────────────────────────────────────────────────────┤
│ [detailBanner]  e.g. "Returned for changes: <reason>"                   │
│ ┌──────────────────────────────────────┐ ┌───────────────────────────┐ │
│ │ [detailSummary] summary cards         │ │ Request status            │ │  app-status-tracker
│ │ (dga-card + dga-info-field)           │ │ Current status (● In rev.)│ │  current card: tag, since, reason
│ │                                       │ │ Status history            │ │
│ ├──────────────────────────────────────┤ │ ● In review  2 Mar, 12:30 │ │  timeline, newest first
│ │ Attachments                           │ │ │ By Reviewer             │ │  actor, reason
│ │ [detailAttachments] (P6)              │ │ ○ Submitted  1 Mar, 11:00 │ │
│ └──────────────────────────────────────┘ ├───────────────────────────┤ │
│                                          │ [detailActions]            │ │  role=group, gated by the screen
│                                          │ [Resubmit] [Cancel request]│ │
│                                          └───────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────┘
States: loading (role=status) and load error (role=alert, reference code, Retry) replace the body.
```

The main column comes first (inline start: right in Arabic); the tracker column wraps below it on a narrow screen.

## Build a screen on it

```ts
readonly history = computed<readonly StatusHistoryEntry[]>(() =>
  (this.store.record()?.history ?? []).map((item) => ({
    statusKey: RequestStatusLabelKeys[item.statusId],        // from the status lookup constants, never a display label
    at: item.changedAt,                                      // ISO instant from the API
    actorLabel: item.changedByName,
    reasonText: item.reason ?? undefined,
  })));
readonly toneFor: StatusToneResolver = (statusKey) => REQUEST_STATUS_TONES[statusKey] ?? 'neutral';
```

```html
<app-requester-detail-pattern
  titleKey="requests.detail.title"
  [recordNumber]="store.record()?.number ?? null"
  [statusKey]="statusKey()" [statusTone]="toneFor(statusKey())" [createdAt]="store.record()?.createdAt ?? null"
  [history]="history()" [toneFor]="toneFor"
  [loading]="store.loading()" [error]="store.loadError()" (retry)="store.reload()">
  @if (store.isReturned()) {
    <dga-alert detailBanner variant="warning">{{ 'requests.detail.returned' | t }}</dga-alert>
  }
  <div detailSummary>
    <dga-card [heading]="'requests.detail.applicant' | t">...</dga-card>
  </div>
  <div detailActions>
    <dga-button *hasPermission="PermissionKeys.RequestsResubmit" [disabled]="!store.canResubmit()" (click)="resubmit()">
      {{ 'requests.detail.resubmit' | t }}
    </dga-button>
    <dga-button *hasPermission="PermissionKeys.RequestsCancel" variant="secondary-outline"
                [disabled]="!store.canCancel()" (click)="openCancelDialog()">
      {{ 'requests.detail.cancel' | t }}
    </dga-button>
  </div>
</app-requester-detail-pattern>
```

## Contract

| Detail input | Meaning |
| --- | --- |
| `titleKey` (required) | Translation key of the page title |
| `recordNumber` | The record's human-readable number (data, shown as is) |
| `statusKey`, `statusTone` | Translation key of the record's status and its `DgaStatusTone` |
| `createdAt` | ISO instant; shown in the app language |
| `history`, `toneFor`, `trackerTitleKey` | Passed to the status tracker |
| `loading`, `error`, `translatedCodes` | Load state; an error wins over loading so its reference code stays readable |
| `retry` (output) | Retry pressed in the error state |

Slots: `[detailBanner]` (above the columns), `[detailSummary]` (summary cards), `[detailAttachments]` (renders an
"Attachments" section only when present), `[detailActions]` (renders a labelled action group only when present).

| Tracker input | Meaning |
| --- | --- |
| `entries` | `StatusHistoryEntry[]`: `statusKey` (translation key), `at` (ISO), `actorLabel?`, `reasonText?` |
| `titleKey` | Default "Request status" |
| `toneFor` | Status key to `DgaStatusTone`; default neutral |
| `order` | `HistoryOrder.NewestFirst` (default) or `OldestFirst`; equal instants keep the API order |
| `current` | Overrides the current entry (default: the latest by `at`) |
| `headingLevel` | `StatusTrackerHeadingLevel.Page` (2, default) or `Section` (3) |

## Rules

- **Presence is permission, enablement is status.** An action the user may never take is not projected
  (`*hasPermission`); an action the current status does not allow stays visible and disabled. The pattern never
  decides either.
- Status names are translation keys built from the status lookup constants, never the API's display label; tones come
  from one screen-level map per workflow.
- Actor names and reasons are data: shown as sent, never translated, never used as keys.
- Dates are ISO instants from the API; the pattern formats them in the app language. Never format on the server.
- Attachments arrive in P6 through the attachment adapter; until then leave `[detailAttachments]` out.

## Do and don't

| Do | Don't |
| --- | --- |
| Show the reason of a return or rejection in a `[detailBanner]` alert and in the tracker | Hide the reason behind a click |
| Keep one tone map for the workflow and reuse it in the list, the header and the tracker | Pick tones per screen |
| Open the wizard pre-filled for a resubmission | Edit the submitted request in place on this page |
| Let the screen reload after an action (cancel, resubmit) | Patch the history locally |

## Accessibility

The page is an `article` named by its `h1`; record number and creation date are a description list; the status is
read with a hidden "Status" label before the tag. The tracker is a region named by its heading; the timeline is an
ordered list whose current entry has `aria-current="true"`, its markers are decorative, every date is a `time` with a
machine-readable `datetime`, and nothing in it takes focus. Loading is `role="status"`, the load error `role="alert"`
with a native Retry button. The action group is `role="group"` named "Actions"; its buttons keep their DOM order,
mirrored in Arabic.
