# Business requirements: citizen complaints

Input of the Complaint pipeline proof (`tools/pipeline-proofs`). Fictional; no real organization.

## Background

Citizens complain about a ministry service by phone or e-mail today. Complaints get lost, nobody knows how long a
complaint has been open, and citizens cannot see what happened to theirs. The ministry wants one place where a
citizen files a complaint and follows it, and where the complaints team works through them.

## Actors

- **Citizen:** signed in to the portal; files complaints and follows their own.
- **Complaints officer:** internal staff who handles complaints from a queue.
- **Complaints manager:** internal staff who handles complaints that were not handled in time.

## Requirements

1. A citizen files a complaint in a short multi-step form: the service concerned (from a reference list of
   services), a complaint category (from a reference list: delay, staff conduct, wrong decision, other), a subject
   (at most 150 characters), a description (at most 2000 characters) and an optional supporting file. The last step
   shows everything for review before the complaint is submitted. The citizen may save a draft and come back.
2. A submitted complaint gets a reference number that the citizen sees at once.
3. A complaints officer picks a complaint from the queue and either **resolves** it with a written response to the
   citizen, or **rejects** it with a reason (for example, not a complaint about a ministry service). A response or a
   reason is always required.
4. The officer may **ask the citizen for more information** with a question; the complaint then waits for the
   citizen, who answers once and the complaint returns to the queue.
5. A complaint that is neither resolved nor rejected **10 days** after it was submitted is **escalated** to the
   complaints manager, who then resolves or rejects it.
6. Citizens see only their own complaints; officers and the manager see all complaints.
7. Every status change, with who made it and when, is visible in the complaint's timeline.
8. All screens are available in Arabic and English.

## Screens

- **File a complaint** (citizen): the multi-step form of requirement 1. It must look like the target images in this
  folder: `target.<language>.<width>.png` (Arabic and English, desktop 1440 px and mobile 390 px). The targets show the
  starter's multi-step wizard pattern; the stepper, field layout and action bar are the look to match. Dynamic and
  branding areas are listed in `masks.json`.
- **My complaints** (citizen): a list with status and reference number; opening one shows its detail and timeline.
- **Complaints queue and detail** (officer, manager): the queue, then the detail with the decision actions.

## Out of scope

Anonymous complaints, satisfaction surveys after resolution and integration with other complaint channels.
