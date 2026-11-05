# Shared (`src/app/shared`)

This folder is now a module: read [MODULE.md](MODULE.md) (what lives here, how to use and extend it) and its
generated [CLAUDE.md](CLAUDE.md) (the rules for edits).

It holds presentational building blocks that two or more features use and that are not page patterns
(`src/app/patterns`) or `@codelab/ng-dga` components. The first one is the `*hasPermission` structural directive
(`permissions/has-permission.directive.ts`).

Rules for anything added here:

- It may import only `core` and `@codelab/ng-dga` (the ESLint boundary preset in `@codelab/ng-tools` enforces it).
  Never import a feature, a layout or a pattern.
- No business logic and no API calls: a shared component renders inputs and emits outputs; services belong in
  `core`.
- No raw text: every label comes from a translation key through the `t` pipe.
- Design tokens and logical CSS only.
- Each item ships with a spec and a row in [MODULE.md](MODULE.md).
- Before adding something, check whether it belongs in `@codelab/ng-dga` instead (a generic DGA component) and ask
  the platform owner.
