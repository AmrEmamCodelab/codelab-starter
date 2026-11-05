# Recipes

Step-by-step guides for recurring changes. Each recipe names the generator that does the mechanical part, what to
fill in by hand (business logic only), the tests to extend and how to verify. The scaffold skills in
`.claude/skills/` run these recipes; a module's `module.rules.yaml` lists the recipes that apply to it (`recipes:`).

| Recipe | Generator | Skill |
| --- | --- | --- |
| [Add an endpoint](add-endpoint.md) | `node tools/scaffold/new-endpoint.mjs` | `new-endpoint` |
| [Add a permission](add-permission.md): add a key to both catalogs (C# + generated `PermissionKeys`) and gate the endpoint, route and buttons | `node tools/scaffold/new-permission.mjs` | `new-permission` |

The other scaffold generators are documented in [tools/scaffold/README.md](../../tools/scaffold/README.md):
`new-module.mjs` (a new business area) and `new-permission.mjs` (a new permission key).

Planned recipes arrive with their phases: the deadline (SLA) recipe, new screens with the page patterns, status
transitions and lookups.
