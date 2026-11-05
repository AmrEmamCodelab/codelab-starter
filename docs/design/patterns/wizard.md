# Pattern: multi-step wizard

**Use it for** a requester filling in a request that is too long for one page: the request is split into steps (who
is asking, what for, the documents), checked step by step, reviewed and submitted. A draft can be saved on the way.

**Do not use it for** a short form (one `app-editor-dialog-pattern` or a single page form), a staff decision (use the
[staff detail + decision dialog](staff-detail.md)) or a read-only view of a submitted request (use the
[requester detail](requester-detail.md)).

| Part | Selector / export | Code |
| --- | --- | --- |
| Wizard | `app-wizard-pattern` | `src/app/patterns/wizard/wizard-pattern.component.*` |
| Step body / review slots | `<ng-template wizardStep="id">`, `<ng-template wizardReview>` | `wizard/wizard-templates.directive.ts` |
| Navigation guard | `guardNext`, `guardStepChange`, `guardSubmit`, `firstInvalidStep`, `WizardStepDef` | `wizard/wizard-navigation.ts` |
| Server errors to steps | `WizardFieldSteps`, `errorsByStep`, `stepIdForField`, `firstStepWithErrors` | `wizard/wizard-errors.ts` |
| Step trail | `dga-stepper` (ng-dga, navigable) | `packages/npm/ng-dga/src/lib/stepper/` |

API reference: [patterns/MODULE.md](../../../frontend/starter-web/src/app/patterns/MODULE.md).

## Anatomy

```
┌───────────────────────────────────────────────────────────────────┐
│ (1)✓ Applicant ── (2) Request ── (3) Documents ── (4) Review       │  dga-stepper: navigable, aria-current="step"
├───────────────────────────────────────────────────────────────────┤
│ [!] The request could not be saved.  Reference code: K7Q2-91      │  error alert (role=alert): message, field
│     Check the following:  • ...   [Go to Documents]               │  errors of this step, other steps with errors
│ Step 2 of 4                                                        │
│ Request                                         ← focus on change  │  step heading (tabindex=-1)
│ [!] Complete the required fields in this step before you continue. │  shown after a refused Next / Submit
│ <ng-template wizardStep="request"> (the screen's fields)           │
├───────────────────────────────────────────────────────────────────┤
│ [Cancel] [‹ Back]                          [Save draft] [Next ›]   │  Submit replaces Next on the last step
└───────────────────────────────────────────────────────────────────┘
```

A vertical trail (`orientation="vertical"`) sits at the inline start (right in Arabic) beside the body and moves above
it on a narrow screen. A horizontal trail narrower than 36rem compacts by itself (bullets plus the current label).

## Build a screen on it

```ts
// The screen owns the form; the wizard only reads each section's validity.
readonly form = this.fb.group({ applicant: this.fb.group({...}), request: this.fb.group({...}), documents: this.fb.group({...}) });
private readonly status = toSignal(this.form.statusChanges, { initialValue: this.form.status });

readonly steps: readonly WizardStepDef[] = [
  { id: 'applicant', titleKey: 'requests.new.steps.applicant', valid: computed(() => this.validOf('applicant')) },
  { id: 'request', titleKey: 'requests.new.steps.request', descriptionKey: 'requests.new.steps.request-hint',
    valid: computed(() => this.validOf('request')) },
  { id: 'documents', titleKey: 'requests.new.steps.documents', valid: computed(() => this.validOf('documents')) },
];
// Server field paths (or prefixes) to the step that owns them.
readonly fieldSteps: WizardFieldSteps = { applicant: 'applicant', request: 'request', documents: 'documents' };
readonly canSaveDraft = computed(() => this.features.draftsEnabled() && this.permissions.has(PermissionKeys.RequestsDraftSave));
```

```html
<app-wizard-pattern
  [steps]="steps" [(step)]="store.step" [fieldSteps]="fieldSteps"
  [busy]="store.saving()" [error]="store.saveError()"
  [draftEnabled]="canSaveDraft()" cancellable
  (next)="store.autosave()" (saveDraft)="store.saveDraft()" (submit)="store.submit()" (cancel)="leave()">
  <ng-template wizardStep="applicant" let-errors="errors">
    <dga-text-input [label]="'requests.new.fields.email' | t" [(value)]="store.email"
                    [error]="errors | fieldError: 'applicant.email'" />
  </ng-template>
  <ng-template wizardStep="request" let-errors="errors">...</ng-template>
  <ng-template wizardStep="documents" let-errors="errors">...</ng-template>
  <ng-template wizardReview>
    <app-request-summary [request]="store.draft()" />          <!-- read-only summary of every step -->
  </ng-template>
</app-wizard-pattern>
```

## Contract

| Input / model | Meaning |
| --- | --- |
| `steps` (required) | `WizardStepDef[]`: `id`, `titleKey`, `descriptionKey?`, `valid` (a signal of that step's own validity) |
| `step` (model) | Zero-based current step, two-way; clamped. The screen may set it (resume a draft at step 3) |
| `linear` | Default `true`: the trail revisits earlier or completed steps only, and a forward jump needs every step on the way valid. `false`: jump anywhere |
| `orientation` | `'horizontal'` (default) or `'vertical'` trail |
| `labelKey`, `reviewTitleKey`, `submitKey` | Translation keys; defaults "Request steps", "Review", "Submit" |
| `draftEnabled`, `cancellable` | Show Save draft / Cancel (the screen decides: feature switch, permission) |
| `busy` | A save or submit is running: every action is refused, Submit shows a spinner |
| `error`, `translatedCodes` | The failed save or submit (`ApiError`); message, reference code and field errors |
| `fieldSteps` | Server field path or prefix to step id; the longest prefix wins, case is ignored |

| Output | When |
| --- | --- |
| `next` | `{ fromId, toId }` after Next passed the guard (the current step is valid). Autosave here |
| `back` | `{ fromId, toId }`; Back never validates |
| `saveDraft` | Current step id; never validated (a draft may be incomplete) |
| `submit` | Every step is valid. Otherwise the wizard moves to the first invalid step and flags it |
| `cancel` | Cancel pressed (only with `cancellable`) |

Slots: `<ng-template wizardStep="<id>" let-stepId let-errors="errors">` per step (only the current one is rendered)
and `<ng-template wizardReview>` (appends the review step, id `review`).

## Rules

- Keep the entered values in the screen's form or store, never in the step template: only the current step is
  rendered, so a step's own component state is lost when the user moves on.
- `valid` is the step's OWN validity. Cross-step rules belong to the server and come back as field errors.
- The server is the authority: submit sends everything, and a 400 with problem+json field errors is passed as `error`.
  Map every server field to its step in `fieldSteps`; an unmapped field is listed in the summary, never dropped.
- Gate the route, Save draft and Submit with permissions in the screen (submit usually also needs the draft-save
  permission when it saves first). The wizard checks nothing.
- One wizard per page. The page header (title, breadcrumb) is the screen's, above the wizard.

## Do and don't

| Do | Don't |
| --- | --- |
| Build each step's validity from its own form group (`computed` over `statusChanges`) | Copy a monolithic "wizard forms" file: each screen declares only its own steps and fields |
| Autosave on `next` when drafts are on, and show the save state in the screen | Validate on Save draft |
| Put a read-only summary in the review step, with links back to each step | Put editable fields in the review step |
| Use `linear` (default) for requests whose later steps depend on earlier answers | Hide or reorder steps while the user is in the wizard |

## Accessibility

The trail is an ordered list named by `labelKey`, the current step has `aria-current="step"`, and each step's state
(completed, current, needs attention) is read as text, not only shown by colour. Trail buttons use a roving tabindex:
Tab reaches the current step, the arrow keys move along the trail (mirrored in Arabic; Up/Down when vertical),
Home/End jump to the ends, unreachable steps are disabled. After every step change focus moves to the new step's
heading, so the change is announced. Errors and a refused Next are `role="alert"`; the field messages sit under their
fields through the `fieldError` pipe. The action bar uses native buttons in a fixed order (Back at the start, Next or
Submit at the end), mirrored in Arabic.
