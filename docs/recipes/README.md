# Recipes

Step-by-step guides for recurring changes. Each recipe names the generator that does the mechanical part, what to
fill in by hand (business logic only), the tests to extend and how to verify. The scaffold skills in
`.claude/skills/` run these recipes; a module's `module.rules.yaml` lists the recipes that apply to it (`recipes:`).

| Recipe | Generator | Skill |
| --- | --- | --- |
| [Add an endpoint](add-endpoint.md) | `node tools/scaffold/new-endpoint.mjs` | `new-endpoint` |
| [Add a permission](add-permission.md): add a key to both catalogs (C# + generated `PermissionKeys`) and gate the endpoint, route and buttons | `node tools/scaffold/new-permission.mjs` | `new-permission` |
| [Add a lookup](add-lookup.md): a bilingual reference-data list seeded by a migration (ids constants, type key, seed migration) | `node tools/scaffold/new-lookup.mjs` | `new-lookup` |
| [Add a notification](add-notification.md): a template key, a Notifications migration with the Arabic and English texts per channel, one call to INotificationService | none (migration by hand) | - |
| [Add a background job](../../backend/src/Modules/Jobs/MODULE.md#extend): IJob with key and cron constants, AddStarterJob, unit tests | none (by hand) | none |

The other scaffold generators are documented in [tools/scaffold/README.md](../../tools/scaffold/README.md):
`new-module.mjs` (a new business area) and `new-permission.mjs` (a new permission key).

Planned recipes arrive with their phases: the deadline (SLA) recipe, new screens with the page patterns, status
transitions.
