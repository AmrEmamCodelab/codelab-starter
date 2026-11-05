# Shared (`src/app/shared`)

Empty on purpose. This folder is for presentational building blocks that two or more features use and that are not
page patterns (`src/app/patterns`) or `@codelab/ng-dga` components.

Rules for anything added here:

- It may import only `core` and `@codelab/ng-dga` (the ESLint boundary preset in `@codelab/ng-tools` enforces it).
  Never import a feature, a layout or a pattern.
- No business logic and no API calls: a shared component renders inputs and emits outputs; services belong in
  `core`.
- No raw text: every label comes from a translation key through the `t` pipe.
- Design tokens and logical CSS only.
- Each item ships with a spec, and the first item added turns this folder into a module: add a
  `module.rules.yaml` and a `MODULE.md` (Purpose, Use, Extend, Rules, Gotchas).
- Before adding something, check whether it belongs in `@codelab/ng-dga` instead (a generic DGA component) and ask
  the platform owner.
