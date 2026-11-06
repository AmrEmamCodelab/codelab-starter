// The public API of the DevAutofill dev tool. Everything runtime comes from dev-autofill.impl.ts, which a
// production build replaces with dev-autofill.stub.ts; the rest is types only, so nothing else reaches the bundle.
export { DevAutofillComponent, DevAutofillDirective, provideDevAutofill } from './dev-autofill.impl';
export type { DevAutofillSet, DevAutofillSummary, DevAutofillValueFactory } from './dev-autofill.model';
