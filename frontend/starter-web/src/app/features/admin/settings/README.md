# Runtime settings screen (`features/admin/settings`)

Part of the `features/admin` module (WebAdmin; its [MODULE.md](../MODULE.md) is the module's doc). This folder holds
the runtime settings screen, built on the page patterns ([patterns/MODULE.md](../../../patterns/MODULE.md)) over the
backend's settings endpoints (`GET api/admin/settings`, `PUT api/admin/settings/{key}`).

## Purpose

| Screen | Route | Key | Pattern |
| --- | --- | --- | --- |
| Every runtime setting, grouped (ordered by group, then key), with a group filter and search; value shown by type (text, number, on / off, "N seconds"), description in the current language, last change (or "Default value"); edit one value in a type-aware dialog with "Use the default" | `/admin/settings` | `settings.view` (edit `settings.manage`) | admin list + editor dialog |

A saved value applies on every API node within the backend's cache bound (30 seconds); the row shows the server's
answer at once.

| Piece | What it does | File |
| --- | --- | --- |
| `AdminSettingsClient`, `SettingsApiPaths` | The two endpoints over `ApiService`; quiet calls (refusals shown inline) | `admin-settings.client.ts` |
| `SettingValueType`, `RuntimeSetting` | The wire shapes (value type as its integer) | `setting.models.ts` |
| `SettingDraft`, `settingValueProblem`, `settingValueLabel` | The editor's draft per value type (text kept as typed; numbers trimmed; boolean as `true` / `false`), the client check (whole number in the int range; duration 0 or more seconds), the list label | `setting-value.ts` |
| `SettingsPageStore` | Load (latest answer wins, a failure clears the rows), client-side group filter, search and paging, the editor and save | `settings-page.store.ts` |
| `SettingsPageComponent` | Renders the store | `settings-page.component.*` |
| `SETTINGS_ROUTES`, `SettingsPaths`, `SETTINGS_MOUNT_SEGMENT` | The route (gated by `settings.view`) and its link | `settings.routes.ts`, `settings.paths.ts` |
| `SettingsPermissionKeys` | `settings.view` / `settings.manage` until `npm run api:gen` adds them to `PermissionKeys` | `settings-permissions.ts` |
| `SettingsTranslationKeys` | Every text (`admin.settings.*`) | `settings-translation-keys.ts` |

## Use

- Mount: `ADMIN_ROUTES` adds `{ path: AdminRouteSegments.Settings, loadChildren: () => import('./settings/settings.routes').then(m => m.SETTINGS_ROUTES) }`
  where `AdminRouteSegments.Settings` is `SETTINGS_MOUNT_SEGMENT`; the admin home's tile links `SettingsPaths.list()`.

## Extend

- **A new value type:** a `SettingValueType` member with the backend's value, a branch in `SettingDraft.value`,
  `settingValueProblem` and `settingValueLabel`, a hint in `SettingValueHintKeys`, the field in the dialog, texts in
  both bundles, specs.
- **A new setting** needs no frontend change: the backend declares it and the list shows it.
- **A new setting group** (a new `*Group` constant in the server's `SettingsSeed`): add `admin.settings.groups.<group>`
  to `ar.json` and `en.json` and to `SettingsTranslationKeys.Groups`; `settings-translations.spec.ts` fails until you do.

## Rules

- The client check only saves a round trip; the server checks the type again (400 with a `value` field error).
- Labels come from i18n (on / off, seconds), never from member names. Keys are server data, shown as they are, and
  render left-to-right (`data-fixed-direction`). A group shows by its `admin.settings.groups.<group>` text
  (`settingGroupName`), else as the server sent it.

## Gotchas

- `settings-permissions.ts` casts the two keys to `PermissionKey`: after `npm run api:gen` the integrator replaces
  every `SettingsPermissionKeys.*` with `PermissionKeys.SettingsView` / `PermissionKeys.SettingsManage` and deletes
  the file.
- `settings-translations.spec.ts` checks the bundles for every key of `SettingsTranslationKeys` plus the home tile.
