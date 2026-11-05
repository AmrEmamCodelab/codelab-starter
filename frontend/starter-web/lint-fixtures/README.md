# lint-fixtures

A miniature app tree (`src/app/{core,shared,layouts,patterns,features,showcase}`) that `tools/lint-fixtures.spec.ts`
lints with the app's ESLint config to prove the guardrails work.

- `*.ts` files are clean import targets and must pass.
- `*.ts.fixture` files are the cases: the spec lints each one as the `.ts` file it stands for. Deliberate violations
  (HttpClient in a feature, shared, a pattern or the showcase importing a feature, a third-party UI kit, inline styles)
  live only in `.fixture` files, so the repository content rules (`tools/rules-engine/check-content.mjs`) and the
  normal `npm run lint` never see them.

Lint proof (phase 4): a feature importing `@codelab/ng-dga`, patterns, layouts and shared passes
(`feature-imports-*.component.ts.fixture`), and so does the showcase importing ng-dga, patterns, layouts, shared and
core (`showcase/gallery/showcase-imports-everything.component.ts.fixture`). Third-party UI imports are not fixture
files: the spec writes one import per library (Angular Material, PrimeNG, NG-ZORRO and both external DGA scopes) into
each of features, patterns, shared, layouts and showcase and lints it as text, because the scrub sweep refuses the
external DGA scopes written literally in any file. The hook half of the proof is `.claude/hooks/tests/fe-lint-proof.test.mjs`.

Permission-gate cases (`base.fe-permission-gated`): `features/orders/orders-actions.component.html` must pass the
template gate; each `ungated-*.component.{html,ts}.fixture` case (a button, an inline-template button, a submit button in a
submitting form, on-click, (keyup.enter), an anchor with (click), a role=button element and a (confirm) dialog host)
must fail on one line. `features/orders/orders.routes.ts` holds a gated, an ungated and an unguarded (key but no tagged
permission guard) route table for the route-table gate;
route tables are runtime data that no content rule sees, so both are plain `.ts` files.

The folder is ignored by `eslint.config.mjs` and `stylelint.config.mjs`. Never import from it in app code.
