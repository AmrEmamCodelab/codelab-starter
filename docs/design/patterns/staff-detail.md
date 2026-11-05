# Pattern: staff detail + decision dialog

**Use it for** one record a staff member reviews and decides on: the record's data in cards, its history, and the
decisions the server allows now (approve, return for changes, reject with a reason).

**Do not use it for** a requester following their own record (use the [requester detail](requester-detail.md)) or
editing reference data (use the [admin list](admin-list.md) dialog).

| Part | Selector | Code |
| --- | --- | --- |
| Detail page | `app-staff-detail-pattern` | `src/app/patterns/staff-detail/staff-detail-pattern.component.*` |
| Allowed actions | `AllowedAction`, `ActionTone`, `decisionButtons`, `findAllowedAction` | `src/app/patterns/staff-detail/allowed-actions.ts` |
| Decision dialog | `app-decision-dialog` | `src/app/patterns/decision-dialog/decision-dialog.component.*` |
| Reason rules | `reasonProblem`, `ReasonProblem`, `DECISION_REASON_MIN_LENGTH` / `MAX_LENGTH` | `src/app/patterns/decision-dialog/decision-reason.ts` |
| Keys | `StaffDetailTranslationKeys`, `DecisionDialogTranslationKeys` | `*-translation-keys.ts` in each folder |

## Anatomy

```
┌──────────────────────────────────────────────────────────────────┐
│ ‹ Back to the queue                                             │  [detailBack]
│ Request R-0042   <dga-status-tag>             [header actions]  │  titleKey + titleParams, [detailStatus], [detailHeaderActions]
├──────────────────────────────────────────────────────────────────┤
│ <dga-card> applicant   <dga-card> request data   ...            │  default slot: the screen's cards
│ History                                                          │  historyLabelKey + [detailHistory] (status tracker)
├──────────────────────────────────────────────────────────────────┤
│ Decision                    [Return] [Reject]      [Approve]    │  the server's allowed actions, in tone order
└──────────────────────────────────────────────────────────────────┘
States: loading (role=status), error (role=alert, reference code, Retry), "no action available".

Decision dialog (for an action with requiresReason):
┌──────────────────────────────── Reject request ─────────────[x]┐
│ [server error alert with field errors]                          │
│ Explanation (messageKey)                                        │
│ Reason *                                                        │
│ ┌──────────────────────────────────────────────────────────┐    │
│ └──────────────────────────────────────────────────────────┘    │
│ Enter at least 5 characters.                    12 of 500       │
│                                      [Cancel]  [Reject]         │
└─────────────────────────────────────────────────────────────────┘
```

## Allowed actions

The server computes the actions from the record's workflow state AND the caller's permissions and sends them with
the record:

```ts
interface AllowedAction {
  readonly code: string;            // the screen's constant for the transition ('approve')
  readonly labelKey: string;        // the button's translation key
  readonly requiresReason: boolean; // open the decision dialog first
  readonly tone: ActionTone;        // 'primary' | 'neutral' | 'destructive'
}
```

The panel shows exactly these actions (one per code), neutral side steps first, refusals next and the expected step
(primary) last at the inline end. A user therefore never sees a button the API would refuse; the API still checks.
With no allowed action the panel says so.

## Build a screen on it

```html
<app-staff-detail-pattern
  titleKey="cases.detail.title" [titleParams]="{ reference: store.record()?.reference ?? '' }"
  [actions]="store.record()?.allowedActions ?? []" [busy]="store.deciding()"
  [loading]="store.loading()" [error]="store.loadError()"
  (decide)="onDecide($event)" (retry)="store.reload()">
  <a detailBack [routerLink]="queueLink">{{ 'cases.detail.back' | t }}</a>
  <dga-status-tag detailStatus [tone]="statusTone()">{{ statusLabelKey() | t }}</dga-status-tag>
  <dga-card [heading]="'cases.detail.applicant' | t">...</dga-card>
  <app-status-tracker detailHistory [entries]="store.history()" />
</app-staff-detail-pattern>

<app-decision-dialog
  [open]="pending() !== null" [titleKey]="pending()?.labelKey ?? ''" [confirmKey]="pending()?.labelKey ?? ''"
  [destructive]="pending()?.tone === ActionTone.Destructive"
  [busy]="store.deciding()" [error]="store.decideError()" [(reason)]="reason"
  (confirm)="store.decide(pending()!, $event)" (cancel)="pending.set(null)" />
```

```ts
onDecide(action: AllowedAction): void {
  if (action.requiresReason) {
    this.reason.set('');
    this.pending.set(action);       // the dialog collects the reason
    return;
  }
  this.store.decide(action, null);  // calls the API with the action code, then reloads the record
}
```

After a successful decision the screen closes the dialog (`pending.set(null)`) and reloads the record, which brings
the new status, history and allowed actions.

## Contract

| Detail input / output | Meaning |
| --- | --- |
| `titleKey` (required), `titleParams` | The page heading (`h1`) |
| `actions` | `readonly AllowedAction[]` as the server sent them |
| `busy` | A decision is in progress: every decision button is disabled and `decide` is not emitted |
| `loading`, `error`, `translatedCodes` | Load state; the error shows its reference code and a Retry button |
| `decisionLabelKey`, `historyLabelKey` | Section headings; `historyLabelKey = null` hides the history section |
| `decide` (output) | The chosen `AllowedAction` |
| `retry` (output) | Retry in the error state |

Slots: `[detailBack]`, `[detailStatus]`, `[detailHeaderActions]`, default (cards), `[detailHistory]`.

| Dialog input / model / output | Meaning |
| --- | --- |
| `open`, `titleKey` (required), `messageKey` + `messageParams` | Controlled like the editor dialog; optional explanation above the field |
| `reasonLabelKey`, `confirmKey`, `cancelKey`, `destructive` | Labels and the confirm tone |
| `minLength`, `maxLength` | Reason limits after trimming (defaults 5 and 500); keep them equal to the server validator |
| `busy`, `error`, `fieldErrors`, `translatedCodes`, `reasonField` | Server state; a field error for `reasonField` (default `reason`) shows under the field |
| `reason` (model) | The text as typed |
| `confirm` (output) | The trimmed reason, emitted only when it is valid |
| `cancel` (output) | Cancel button, close icon or Escape |

Reason problems (`ReasonProblem`, integers): `Required = 1`, `TooShort = 2`, `TooLong = 3`. The message appears
after the first confirm or when the field loses focus, never while the user starts typing; it is forgotten when the
dialog opens again.

## Rules

- Action codes, status ids and their labels come from the module's constants and i18n; the pattern never compares
  translated text and never decides which action is allowed.
- The decide endpoint validates the reason with the same limits and returns problem+json field errors under
  `reason`.
- The screen gates the route with `permissionGuard()`; header actions it adds are gated with `*hasPermission`.

## Accessibility

An `article` named by the `h1`, named sections for the cards, history and decision panel, the decision buttons as a
labelled `role="group"` of native buttons, loading as `role="status"` and errors as `role="alert"`. The dialog is the
editor dialog (native modal `dialog`, focus trap and restore, Escape): focus starts in the reason field, which is a
labelled, `required` `textarea` with its counter and message linked by `aria-describedby` and `aria-invalid` when
refused; a refused confirm moves focus back to it.
