// The app's DevAutofill data-set registry: every feature's dev data sets, and nothing else. Development builds only:
// the production configuration replaces this file with shared/dev-autofill/dev-autofill.sets.stub.ts (angular.json
// fileReplacements), so no set value reaches the production bundle. Keep the export name.
import type { DevAutofillSet } from './shared/dev-autofill';
import { SERVICE_REQUESTS_DEV_AUTOFILL_SETS } from './features/service-requests/dev/service-request-autofill'; // codelab:module ServiceRequests

export const APP_DEV_AUTOFILL_SETS: readonly DevAutofillSet[] = [
  ...SERVICE_REQUESTS_DEV_AUTOFILL_SETS, // codelab:module ServiceRequests
];
