# DevAutofill (`src/app/shared/dev-autofill`)

## Purpose

A development-only tool that fills any reactive form with a named, known-good data set at the press of a button,
so a developer or tester walks a long wizard in seconds. It is generic: a feature registers data sets for its form
key; the tool knows no field and no feature. A production build contains none of it: the real implementation and
the app's data-set registry are replaced by stubs at build time, and `tools/ci/no-dev-autofill.mjs` proves it on the
built bundle.

| File | Role |
| --- | --- |
| `index.ts` | Public API: `provideDevAutofill`, `DevAutofillComponent` (`<dga-dev-autofill>`), `DevAutofillDirective` (`[devAutofill]`), types `DevAutofillSet`, `DevAutofillSummary`, `DevAutofillValueFactory` |
| `dev-autofill.impl.ts` | The real implementation (dev builds). Exports `DEV_AUTOFILL_MARKER`, rendered as `data-dev-autofill` on the button group |
| `dev-autofill.stub.ts` | The production stand-in: same exports minus the marker, renders and registers nothing |
| `dev-autofill.sets.stub.ts` | The production stand-in for the app's registry `src/app/dev-autofill.sets.ts`: `APP_DEV_AUTOFILL_SETS = []` |
| `dev-autofill.fill.ts` | The logic: sets for a form, value factories, set validation, writing a set into a `FormGroup` |
| `dev-autofill.model.ts` | `DevAutofillSet`, `DevAutofillSummary`, the `DEV_AUTOFILL_SETS` multi token |
| `dev-autofill-keys.ts` | Translation key, test ids, the `devAutofill` label-key segment |

## Use

1. A feature declares its sets in a `dev/` folder, imported by nothing but the app registry:

   ```ts
   // features/<feature>/dev/<form>-autofill.ts
   import type { DevAutofillSet } from '../../../shared/dev-autofill';

   export const TEST_RECORD_AUTOFILL: DevAutofillSet = {
     formKey: 'test-record-form',
     labelKey: 'testRecords.devAutofill.valid', // ar + en text; the devAutofill segment is required
     values: { title: 'A valid title', preferredDate: () => tomorrowIso() }, // a function is called at fill time
   };
   ```

2. The app registry `src/app/dev-autofill.sets.ts` lists every set (one line per feature, with its module marker),
   and `app.config.ts` registers it once: `provideDevAutofill(...APP_DEV_AUTOFILL_SETS)`.
3. The screen places the button next to its form, by element or by directive:

   ```html
   <dga-dev-autofill formKey="test-record-form" [form]="form" (autofilled)="onAutofilled($event)" />
   <form [formGroup]="form" devAutofill="test-record-form">...</form>
   ```

   Each set of the form is one button labelled with its `labelKey`; pressing it writes the values (`patchValue`),
   marks each written control dirty and touched, and emits a `DevAutofillSummary` (filled, skipped disabled, unknown
   paths, still-invalid paths). With no set for the form, nothing is rendered.

Check a production bundle: `node tools/ci/no-dev-autofill.mjs --dist frontend/starter-web/dist/starter-web/browser`.

## Extend

- Another set for a form: add an entry to the feature's `dev/` file and to the registry. Two sets of one form need
  different label keys.
- A value that depends on today or must be unique: give a `DevAutofillValueFactory` (`() => value`).
- A new export: add it to `dev-autofill.impl.ts` AND `dev-autofill.stub.ts` (same name and template API), and to
  `index.ts`. `dev-autofill.stub.spec.ts` fails when the two files' exports differ.
- A new text: a `shared.dev-autofill.*` key in `DevAutofillTranslationKeys` and both `ar.json` and `en.json`.

## Rules

- Production exclusion is by file replacement, never by `isDevMode()`: the production configuration of `angular.json`
  replaces `dev-autofill.impl.ts` with `dev-autofill.stub.ts` and `src/app/dev-autofill.sets.ts` with
  `dev-autofill.sets.stub.ts`. Both are needed: with the implementation stub alone the sets are still evaluated as
  arguments of `provideDevAutofill` and their values reach the bundle (verified on a build).
- Import the tool only through `index.ts`. Never import `dev-autofill.impl.ts`, the model's `DEV_AUTOFILL_SETS` or
  `dev-autofill.fill.ts` from app code: they would bypass the swap. Specs may import them.
- A feature's data sets are referenced only by the app registry, never by the feature's own screens.
- Every data-set `labelKey` has a `devAutofill` segment; `provideDevAutofill` throws otherwise. It is the marker the
  CI check uses to find a leaked set.
- Data sets hold invented, non-personal sample values only (no real names, IDs, phone numbers or e-mail addresses).
- The marker string lives only in `dev-autofill.impl.ts` and `tools/ci/no-dev-autofill.mjs` (their test checks
  that the two are equal). Never put it or a data set in a stub.

## Gotchas

- The directive binds the `FormGroup` of its element once, at init; a screen that swaps `[formGroup]` for another
  instance should use `<dga-dev-autofill [form]>` instead. Without `[formGroup]` on the element it throws.
- Values are written in the set's key order: put a field before the field it drives (category before subcategory).
- A path the form does not have is reported in `unknown`, not thrown; a stale set is visible there and in `invalid`.
- The production check scans `.js`, `.mjs`, `.html` and `.css` only. The `shared.dev-autofill.title` translation is
  in the shipped `ar.json`/`en.json`: text, not code.
- `ng serve` and `ng build --configuration development` include the tool; `ng build` (production by default) does not.
