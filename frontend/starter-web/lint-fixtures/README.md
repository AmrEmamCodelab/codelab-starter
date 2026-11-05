# lint-fixtures

A miniature app tree (`src/app/{core,shared,features}`) that `tools/lint-fixtures.spec.ts` lints with the app's
ESLint config to prove the guardrails work.

- `*.ts` files are clean import targets and must pass.
- `*.ts.fixture` files are the cases: the spec lints each one as the `.ts` file it stands for. Deliberate violations
  (HttpClient in a feature, shared importing a feature, a third-party UI kit, inline styles) live only in `.fixture` files, so the
  repository content rules (`tools/rules-engine/check-content.mjs`) and the normal `npm run lint` never see them.

The folder is ignored by `eslint.config.mjs` and `stylelint.config.mjs`. Never import from it in app code.
