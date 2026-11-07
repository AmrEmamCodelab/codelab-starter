# Business requirements: equipment loans

Sample input for the kit evals (`tools/ai-evals`). Fictional; no real organization.

## Background

The ministry lends field equipment (measuring kits, cameras, safety gear) to organizations that run approved
inspection programmes. Today requests arrive by e-mail and are tracked in a spreadsheet.

## Actors

- **Requester:** an employee of an external organization, signed in to the portal.
- **Equipment officer:** internal staff who reviews loan requests.
- **Store keeper:** internal staff who hands the equipment over and records its return.

## Requirements

1. A requester submits a loan request: equipment category (from a reference list), quantity (1 to 20), start date,
   end date (at most 30 days after the start date) and the purpose (free text, at most 500 characters).
2. The equipment officer approves or rejects the request. A rejection needs a reason, which the requester sees.
3. The equipment officer may return the request to the requester for changes, with a reason; the requester can
   then resubmit it.
4. After approval the store keeper records the hand-over, and later the return of the equipment.
5. A request that is not decided within 3 working days is escalated to the officer's manager.
6. Requesters see only their own organization's requests; officers see all requests.
7. Every status change is visible in the request's timeline.
8. All screens are available in Arabic and English.

## Out of scope

Stock levels, fines for late returns and integration with the asset system.
