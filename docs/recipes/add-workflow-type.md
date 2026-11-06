# Recipe: add a workflow type (rename the sample into your record type)

Turns the `ServiceRequests` sample into your own record type: a requester files it through a wizard, staff work it
from a queue, and it moves through **your** statuses. Modules 7 to 11 (Records, the workflow engine, Assignments,
Deadlines, the detail and oversight views) are generic; a record type module only **configures** them: its record
type key, statuses, transition table, permissions, seeds, SLA and screens. Copy the sample, rename it, change the
table, regenerate the docs, then sign the generated workflow sheet off with the client.

Module docs: [backend/src/Modules/ServiceRequests/MODULE.md](../../backend/src/Modules/ServiceRequests/MODULE.md) (BE)
and [frontend/starter-web/src/app/features/service-requests/MODULE.md](../../frontend/starter-web/src/app/features/service-requests/MODULE.md) (FE).
Engine contracts: the Workflow rows of [Starter.Domain](../../backend/src/Starter.Domain/MODULE.md) and
[Starter.Application](../../backend/src/Starter.Application/MODULE.md); the test kit
[backend/tests/Starter.Workflow.Testing/README.md](../../backend/tests/Starter.Workflow.Testing/README.md).

## 1. Decide it is a new record type

| The change is... | Do |
| --- | --- |
| A new kind of request with its own form, numbers and statuses | **this recipe** |
| One more status or one more step in an existing flow (escalate, put on hold, reopen) | the `new-transition` skill (`node tools/scaffold/new-transition.mjs`), not a new type |
| A new decision on an existing status | `new-transition` with `--action` only |
| A time limit on a status | [add-deadline.md](add-deadline.md) |
| Reference data in a dropdown | [add-lookup.md](add-lookup.md) |

## 2. Choose the names

Pick every name before copying a file, and keep them consistent: `tools/remove-module.ps1` and the fork tools find a
module by them.

| Name | Sample | Example | Where it shows |
| --- | --- | --- | --- |
| Module (PascalCase, plural) | `ServiceRequests` | `PermitApplications` | `backend/src/Modules/<Module>`, `Starter.Modules.<Module>`, the module markers |
| Entity (PascalCase, singular) | `ServiceRequest` | `PermitApplication` | classes `<Entity>Statuses`, `<Entity>Actions`, `<Entity>Workflow` ... |
| Record type key (kebab, singular) | `service-request` | `permit-application` | `RecordTypeRegistration`, the `{recordType}` route segment, `docs/generated/workflows/<key>.md` |
| Number prefix (2 to 4 capitals) | `SR` | `PA` | numbers `PA-2026-001` |
| Permission prefix (kebab, plural) | `service-requests.*` | `permit-applications.*` | `<Module>PermissionKeys` |
| i18n prefix (camelCase of the module) | `serviceRequests.*` | `permitApplications.*` | every key in `ar.json` and `en.json`; `remove-module.ps1` deletes `<camelName>.*` |
| Frontend area (kebab of the module) | `features/service-requests` | `features/permit-applications` | routes, `module.rules.yaml` |
| Attachable record type | `service-request` | `permit-application` | `AttachableRecordTypes` (Starter.Domain.Attachments), attachment type seeds |

## 3. Create the module shell, then copy the sample in

Let the scaffold create the module and register it (it is the only sanctioned editor of `codelab-starter.slnx` and
the host project's module list):

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/scaffold/new-module.mjs --name PermitApplications
```

Then copy the sample's content over the shell and rename it. Copy, never move: the sample stays until step 12.

| Copy from | To |
| --- | --- |
| `backend/src/Modules/ServiceRequests/{Api,Application,Domain,Infrastructure}` | `backend/src/Modules/PermitApplications/...` |
| `backend/tests/Modules/Starter.Modules.ServiceRequests.Tests/*` (not the `.csproj`) | `backend/tests/Modules/Starter.Modules.PermitApplications.Tests/` |
| `frontend/starter-web/src/app/features/service-requests/` | `frontend/starter-web/src/app/features/permit-applications/` |

Rename in file names and contents, whole words, case-sensitive, in this order: `ServiceRequests` to
`PermitApplications`, `ServiceRequest` to `PermitApplication`, `service-requests` to `permit-applications`,
`service-request` to `permit-application`, `serviceRequests` to `permitApplications`, `SERVICE_REQUESTS` to
`PERMIT_APPLICATIONS`, `SERVICE_REQUEST` to `PERMIT_APPLICATION`, and the prefix constant `"SR"` to `"PA"`. Keep the
shell's `MODULE.md`, `module.rules.yaml` and `.csproj` files and merge the sample's into them (references, rules,
sections). Add the test kit to the new test project:
`<ProjectReference Include="..\..\Starter.Workflow.Testing\Starter.Workflow.Testing.csproj" />`.

Search for leftovers before going on: `ServiceRequest`, `service-request`, `serviceRequests` and `SR-` must not
appear in your new folders.

## 4. The record type

`Domain/<Entity>Record.cs` holds the three engine keys: `RecordType`, `NumberPrefix` and `AttachableRecordType` (add
the key to `AttachableRecordTypes` in `backend/src/Starter.Domain/Attachments/`). `<Entity>RecordTypeSource`
(`Application/Workflow/<Entity>EngineSources.cs`) registers them with Records as a `RecordTypeRegistration`, with
the queue permission (who sees the work queue) and the view-own permission (who lists their own records).

## 5. Statuses and the transition table

The table is one place: `Application/Workflow/<Entity>Workflow.cs` (`Statuses` and `Rules`), with its constants in
`Domain/<Entity>Record.cs` (`<Entity>Statuses`, `<Entity>StatusKeys`, `<Entity>Actions`) and the notification
templates in `Application/<Entity>NotificationKeys.cs`.

1. Replace the sample's statuses with yours: `<Entity>Statuses` values 1, 2, 3, ... (explicit, stored in the
   database; never renumber), one `<Entity>StatusKeys` kebab key each. Exactly one initial status; mark the closed
   ones terminal.
2. Write one rule per action and from-status. Every rule names: the action key (an `<Entity>Actions` constant), the
   permission (a `<Module>PermissionKeys` constant), the side (`Requester`, `Staff`, `System`), the reason
   requirement and its field (`Required` needs a field, the dialog blocks a blank reason), the label key, the
   ownership flag (requester actions: `RequiresOwnership: true`) and a **notification decision**:
   `NotificationDecision.None` when nobody is told, on purpose.
3. Never write a status matrix anywhere else. Screens read the allowed actions from the API (`allowedActions` on
   `GET api/records/{recordType}/{id}`), which the engine computes from this table.

The host refuses to start while `TransitionCatalogValidator` reports anything: an unreachable or undefined status,
a rule without a known permission, a required reason without a field, a missing audience wording, a missing
notification decision, no single initial status, a duplicated action from one status, or a rule out of a terminal
status. The exception lists every violation, one per line.

Later changes to the table go through `node tools/scaffold/new-transition.mjs` (status constant, rule entry,
wording keys, fixture test stub, MODULE.md note); see the `new-transition` skill.

## 6. Permissions

Edit `<Module>PermissionKeys` and `<Module>PermissionCatalogSource` (keys `<permission prefix>.<action>`, with
descriptions an administrator understands). The sample's set is a good default: `create` (file, resubmit, cancel
your own), `view-own`, `queue`, `review` (take into review) and `decide` (approve, reject, return). Add more with
[add-permission.md](add-permission.md). Requester rules use `create`; staff rules use `review` or `decide`.

## 7. Seeds (migrations only)

| Seed | How | Sample migration |
| --- | --- | --- |
| The form's lookups (categories, types) | [add-lookup.md](add-lookup.md) (`new-lookup.mjs`) | `Lookups/.../<timestamp>_AddServiceRequestCategoriesLookup.cs` |
| One notification template per key of `<Entity>NotificationKeys` (ar and en, InApp and Email), including the SLA escalation | [add-notification.md](add-notification.md) | `Notifications/.../<timestamp>_AddServiceRequestTemplates.cs` |
| The attachment types (required document, optional supporting files) | [add-attachment-type.md](add-attachment-type.md) | `Attachments/.../<timestamp>_AddServiceRequestAttachmentTypes.cs` |

Write new migrations with your names and new ids; never copy a sample migration's id or timestamp. The module's own
data (the draft fields) lives in its DbContext and its schema (rename the schema and the `InitialCreate` migration).

## 8. SLA

`<Entity>SlaPolicySource` (`Application/Workflow/<Entity>EngineSources.cs`) declares the deadlines: one
`SlaDefinition(RecordType, Status, Duration, DueSoonBefore, EscalationTemplateKey)` per non-terminal status that has
one (the sample: 3 days on Under Review, due soon 1 day before). Details: [add-deadline.md](add-deadline.md).

## 9. The wizard and the validation parity

The wizard's rules are written twice, on purpose: FluentValidation on the server
(`Application/Validation/<Entity>Rules.cs`, `Application/Drafts/<Entity>SubmitValidator.cs`) and hand-mirrored
validators in the frontend (`features/<area>/data/<entity>-rules.ts`, `<entity>-validators.ts`). One fixture keeps
them equal: `features/<area>/data/validation-parity.fixtures.json`
(`{ "today": "...", "cases": [ { "name", "scope": "step1" | "step2" | "submit", "input", "expected": { "valid", "errorFields" } } ] }`),
read by the frontend spec (`validation-parity.spec.ts`) and by the server test (`Validation/ValidationParityTests.cs`).
Change a rule on both sides and add the cases that prove it; both suites must agree on `valid` and the sorted
`errorFields`. The server also refuses a submit that skips a step; keep that API test.

## 10. Screens and routes

The sample's screens cover the day-one set on the page patterns ([docs/design/page-patterns.md](../design/page-patterns.md)):
requester dashboard, my requests, wizard, requester detail (status tracker, timeline, attachments, cancel and
resubmit), staff work queue and staff detail (decision panel, reason dialogs), admin oversight (read-only).

- Export your route constants from the area (the sample's `SERVICE_REQUESTS_REQUESTER_ROUTES`,
  `SERVICE_REQUESTS_STAFF_ROUTES`, `SERVICE_REQUESTS_ADMIN_ROUTES`) and register them in `app.routes.ts`,
  `app-nav.ts` and `features/admin/admin.routes.ts` with a marker on the same line, `// codelab:module <Module>`, so
  `remove-module.ps1` can take them out again. Gate every route and action button with your permission keys.
- Every text key starts with `<i18n prefix>.`: status wording `<prefix>.status.<statusKey>.<requester|staff|admin>`,
  action labels `<prefix>.action.<actionKey>`, add both languages (Arabic first).
- The DevAutofill set (`features/<area>/dev/<entity>-autofill.ts`, `formKey` `<entity>-wizard`) fills the wizard in
  development builds only; production builds swap it out (`tools/ci/no-dev-autofill.mjs`).

A new page beyond the sample's set: [add-page.md](add-page.md).

## 11. Tests

- **Workflow:** walk every path of your table with `TransitionFixture` (`Workflow/<Entity>WorkflowTests.cs`): each
  action from each status, the refusals (no permission, not the owner, a required reason left blank), the terminal
  statuses offering nothing. Assert the catalog validates:
  `Assert.Empty(TransitionCatalogValidator.Validate(catalog, knownPermissionKeys))`.
- **Use cases, validators, HTTP:** keep every sample test, renamed and adapted to your fields.
- **Frontend:** the copied specs, renamed; the parity spec with your fixture.

## 12. Regenerate, review with the client, and delete the sample

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
node tools/rules-engine/compile.mjs                                           # nested CLAUDE.md of the new folders
& $dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated # permissions, endpoints, statuses.md, workflows/
npm run api:gen --workspace frontend/starter-web                              # generated client and PermissionKeys
```

`docs/generated/workflows/<record type>.md` is the sign-off sheet: the Mermaid state diagram, the statuses with their
requester, staff and admin wording keys, and every transition with its permission, reason and notification decision.
`docs/generated/statuses.md` lists the same statuses next to the other status enums. Walk the client through the
sheet; every change they ask for is a change to the table (step 5), then regenerate.

When your type replaces the sample, remove it:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\remove-module.ps1 -Name ServiceRequests
```

It removes the module, its tests, `features/service-requests`, the marked registrations and the `serviceRequests.*`
keys. DocsGen never deletes a file, so `docs/generated/workflows/service-request.md` is then reported `unexpected:`:
delete it and rerun DocsGen. The sample's seed migrations in Lookups, Notifications and Attachments stay (shipped
migrations are never edited); retire their rows with a new migration if the data must go.

## 13. Verify

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
& $dotnet test backend/tests/Modules/Starter.Modules.PermitApplications.Tests
& $dotnet test backend/tests/Starter.Contract.Tests --filter FullyQualifiedName~Workflow   # permission, wording, notification decision per rule
& $dotnet test backend/tests/Starter.Architecture.Tests                                    # boundaries, no sample vocabulary in the engine
& $dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated --check
npx vitest run src/app/features/permit-applications                                        # from frontend/starter-web
```

Then walk the flow end to end in `ar` and `en` (the sample's exit scenarios are a good script): file with an
attachment, claim, review, return with a reason, resubmit, approve, and a rejection that leaves no resubmit action.
