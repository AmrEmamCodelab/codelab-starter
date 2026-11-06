// The PRODUCTION stand-in for the app's data-set registry, src/app/dev-autofill.sets.ts (angular.json production
// fileReplacements). The registry imports every feature's data sets; replacing it keeps their values and label
// keys out of the production bundle, which the impl stub alone cannot do (the sets would still be evaluated as the
// arguments of provideDevAutofill). Keep the export name equal to the registry's.
import type { DevAutofillSet } from './dev-autofill.model';

export const APP_DEV_AUTOFILL_SETS: readonly DevAutofillSet[] = [];
