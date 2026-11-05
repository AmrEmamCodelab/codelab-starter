# lint-fixtures

A miniature app tree (`src/app/{core,shared,features}`) that `tools/lint-fixtures.spec.ts` lints with the app's
ESLint config to prove the guardrails work.

- `*.ts` files are clean import targets and must pass.
- `*.ts.fixture` files are the cases: the spec lints each one as the `.ts` file it stands for. Deliberate violations
  (HttpClient in a feature, shared importing a feature, a third-party UI kit, inline styles) live only in `.fixture` files, so the
  repository content rules (`tools/rules-engine/check-content.mjs`) and the normal `npm run lint` never see them.

Permission-gate cases (`base.fe-permission-gated`): `features/orders/orders-actions.component.html` must pass the
template gate; each `ungated-*.component.{html,ts}.fixture` case (a button, an inline-template button, a submit button in a
submitting form, on-click, (keyup.enter), an anchor with (click), a role=button element and a (confirm) dialog host)
must fail on one line. `features/orders/orders.routes.ts` holds a gated, an ungated and an unguarded (key but no tagged
permission guard) route table for the route-table gate;
route tables are runtime data that no content rule sees, so both are plain `.ts` files.

The folder is ignored by `eslint.config.mjs` and `stylelint.config.mjs`. Never import from it in app code.
