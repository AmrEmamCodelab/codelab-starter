import { InjectionToken, inject, isDevMode } from '@angular/core';
import type { CanMatchFn } from '@angular/router';

/**
 * Whether the attachments demo page exists in this build: true in a development build (`npm start`, run-local.ps1),
 * false in every optimized build. The API's `GET api/attachments/demo-record` answers in Development only, too.
 */
export const ATTACHMENTS_DEMO_ENABLED = new InjectionToken<boolean>('ATTACHMENTS_DEMO_ENABLED', {
  providedIn: 'root',
  factory: () => isDevMode(),
});

/**
 * Lets `attachments/demo` match only in a development build. In any other build the URL falls through to the 404 page
 * and the page bundle is never loaded.
 */
export const attachmentsDemoEnabledGuard: CanMatchFn = () => inject(ATTACHMENTS_DEMO_ENABLED);
