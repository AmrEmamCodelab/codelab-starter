---
feature: site-visit-permit
status: draft
approvedBy:
approvedOn:
inputs:
  - "Scenario: clients request a site-visit permit; the inspector approves; undecided after 5 days it escalates to the manager"
---

<!-- Worked example of a feature spec (docs/features/README.md). It is a draft that was never built: the hand-edit
     log is empty and nothing was reviewed. `node tools/feature-spec/check-spec.mjs` passes on it; the build gate
     (`--require-approved`) refuses it until a human approves it. -->

# Site-visit permit

## Scenario

> Clients request a site-visit permit; the inspector approves; undecided after 5 days it escalates to the manager.

- A **client** (requester side) fills a short wizard: site name, site address, purpose of the visit (a lookup) and
  the requested visit date, then submits. The record gets a number `SVP-<year>-<n>`.
- An **inspector** (staff side) takes the permit from the work queue into inspection, then approves it or rejects it
  with a reason.
- A permit that stays **under inspection for 5 days** escalates: the record moves to *Escalated* and the
  **manager** decides it (approve, or reject with a reason). The inspector and the queue are notified one day before
  the deadline (due soon) through the SLA badge.
- The client may cancel a draft. Approved, rejected and cancelled are final.

Open decision for the approver: the Deadlines module notifies the assignee (or the queue) on a breach
([Deadlines MODULE.md](../../../backend/src/Modules/Deadlines/MODULE.md)). Moving the record to *Escalated*
automatically uses the "another reaction to a breach" extension point listed there (a port the new module
implements, side `System`). Default proposed here: use it, and expect it as hand edit(s) in the log below.

## Inputs

- The scenario sentence above (no BRD, workbook or image). Screens follow the sample's page patterns; no target image,
  so the visual check compares against the pattern baselines in ar and en.

## Modules

- **New module `SiteVisits`** (new-module), record type `site-visit-permit`, number prefix `SVP`, built from the
  sample by the [add-workflow-type recipe](../../recipes/add-workflow-type.md). Own table: site name, site address,
  purpose id, visit date (all nullable while a draft).
- **Lookups** (seed migration only): new lookup `site-visit-purposes` (new-lookup).
- **Notifications** (seed migration only): templates `site-visits.submitted`, `.approved`, `.rejected`,
  `.escalated`, `.sla-escalation`, ar and en, in-app and e-mail ([add-notification recipe](../../recipes/add-notification.md)).
- **Deadlines**: no change to the module; the SLA is declared in `SiteVisits`.

Statuses (explicit int values, never renumbered): Draft 1, Submitted 2, UnderInspection 3, Escalated 4, Approved 5,
Rejected 6, Cancelled 7 (5, 6, 7 terminal).

## Statuses and transitions

| From | Action | To | Permission | Reason required |
| --- | --- | --- | --- | --- |
| Draft | submit | Submitted | site-visits.create | no |
| Draft | cancel | Cancelled | site-visits.create | no |
| Submitted | start-inspection | UnderInspection | site-visits.inspect | no |
| UnderInspection | approve | Approved | site-visits.inspect | no |
| UnderInspection | reject | Rejected | site-visits.inspect | yes |
| UnderInspection | escalate | Escalated | site-visits.escalate | no |
| Escalated | approve | Approved | site-visits.manage | no |
| Escalated | reject | Rejected | site-visits.manage | yes |

`escalate` is side `System` (run on the SLA breach, never offered as a button); the others are `Requester` (submit,
cancel, with ownership) or `Staff`. Every rule carries a notification decision; `cancel` from Draft is
`NotificationDecision.None`.

## Deadlines

| Status | Duration | Due soon before | Escalation template |
| --- | --- | --- | --- |
| UnderInspection | 5 days | 1 day | site-visits.sla-escalation |

Declared as an `ISlaPolicySource` in `SiteVisits` ([add-deadline recipe](../../recipes/add-deadline.md)). On the
breach the reaction runs `escalate` (see the open decision in Scenario).

## Permissions

| Key | Allows | Expected holder |
| --- | --- | --- |
| site-visits.create | create, edit, submit and cancel own drafts | client |
| site-visits.view-own | list and open own permits | client |
| site-visits.queue | see the work queue and permit details | inspector, manager |
| site-visits.inspect | start inspection, approve, reject under inspection | inspector |
| site-visits.manage | approve or reject escalated permits | manager |
| site-visits.escalate | the system escalation on the SLA breach | no role (SYSTEM actor only) |

## Endpoints

| Method | Route | Permission | Request | Response |
| --- | --- | --- | --- | --- |
| POST | /api/site-visits | site-visits.create | none | 201 `{ id }` |
| GET | /api/site-visits/{id}/draft | site-visits.create | none | draft fields |
| PUT | /api/site-visits/{id}/draft | site-visits.create | draft fields + `step` | 204; 400 `VALIDATION_FAILED` |
| POST | /api/site-visits/{id}/submit | site-visits.create | none | 200 `{ number }` |
| GET | /api/site-visits/{id} | site-visits.view-own | none | permit detail |
| POST | /api/records/site-visit-permit/{id}/transitions | per transition (table above) | `{ action, reason }` | 200 (existing Records endpoint, not new) |

All ids are PublicId tokens; errors are problem+json.

## Screens

| Route | Page pattern | Permission | Target image |
| --- | --- | --- | --- |
| /site-visits/new | wizard | site-visits.create | none |
| /site-visits/:id | requester-detail | site-visits.view-own | none |
| /staff/site-visits | work-queue | site-visits.queue | none |
| /staff/site-visits/:id | staff-detail | site-visits.queue | none |

Every string has ar and en keys under `siteVisits.*`; the decision dialog is the staff-detail pattern's
`app-decision-dialog`.

## Rule files

- The new backend module's `module.rules.yaml` (created by new-module), recipes listed: add-endpoint,
  add-permission, add-deadline, add-workflow-type.
- The new frontend feature folder's `module.rules.yaml` (created with the first screen).
- No change to `rules/base.rules.yaml`; no exemption.

## Tests

- Unit: each use case (create, save draft per step, submit, get), the step validators, the SLA policy source (record
  type, status, 5 days, 1 day, template key), the breach reaction (claims once, runs `escalate` once).
- Workflow: TransitionFixture rows for all 8 transitions (allowed, refused without the permission, reason required).
- Seeds: lookup and template migrations equal their constants (both languages, both channels).
- Contract and architecture tests stay green; integration: the Deadlines pattern (advance `TimeProvider` 5 days, run
  the scan, the record is Escalated and one notification row exists).
- Frontend: Vitest specs per page; visual check ar and en, desktop and mobile.

## Docs

- The new module's `MODULE.md` (Purpose, Use, Extend, Rules, Gotchas) and the frontend feature's `MODULE.md`.
- Regenerated: docs/generated/statuses.md, a new sheet in docs/generated/workflows/, docs/generated/permissions.md,
  docs/generated/endpoints.md, docs/generated/error-codes.md, docs/generated/openapi.v1.json.

## Skill plan

1. new-module - `SiteVisits`, then the record type from the sample (add-workflow-type recipe, steps 3 to 4).
2. new-permission - create, view-own, queue, inspect, manage, escalate (before the transitions that name them).
3. new-transition - the 7 statuses and 8 transitions above.
4. deadline-recipe - 5 days on UnderInspection, due soon 1 day, template site-visits.sla-escalation.
5. new-lookup - `site-visit-purposes`.
6. new-endpoint - the 5 typed endpoints.
7. new-screen - wizard, requester detail, work queue, staff detail.

## Hand-edit log

| # | File | Reason |
| --- | --- | --- |

## Review log

Not reviewed: this example was never built.

Iterations: 0
