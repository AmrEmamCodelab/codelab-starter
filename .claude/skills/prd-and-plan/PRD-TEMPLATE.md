# PRD template

Copy into `plans/<slug>-prd.md`. No file paths or code snippets: describe modules and responsibilities (they outlive
renames). A tiny schema, enum or transition table may be inlined when it states a decision more precisely than prose.

```markdown
# PRD: <Feature or initiative name>

> Status: draft | ready-for-agent
> Plan: [plans/<slug>.md](<slug>.md)
> Source: <feature notes, or tracker item ids>

## Problem statement

<The problem, from the user's point of view.>

## Solution

<The solution, from the user's point of view.>

## User stories

1. As a <actor>, I want <capability>, so that <benefit>. <(item #id) in tracker mode>
<A long numbered list: cover every actor (requester, staff reviewer, manager, administrator) the change affects.>

## Implementation decisions

<Modules built or changed, record types, statuses and transitions, deadlines, permission keys, endpoints and screens
(with their page pattern), lookups, schema and API-contract decisions. By module and responsibility, not by file.>

## Testing decisions

<What a good test proves here (external behaviour, not implementation details), the seams chosen and why, and the
existing tests in the same seam to copy.>

## Out of scope

<What this PRD deliberately leaves out.>

## Assumptions and open questions

1. <A decision taken without confirmation, or a material gap. Numbered so the user can answer quickly.>

## Further notes

<Anything else.>
```
