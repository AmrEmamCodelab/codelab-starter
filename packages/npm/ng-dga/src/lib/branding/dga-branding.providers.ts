import { DOCUMENT, type EnvironmentProviders, inject, InjectionToken, makeEnvironmentProviders, provideEnvironmentInitializer } from '@angular/core';
import { applyDgaBranding, type DgaBranding, parseDgaBranding } from './dga-branding';

/**
 * The project's branding, when the app provides one with {@link provideDgaBranding}. Inject it optionally
 * (`inject(DGA_BRANDING, { optional: true })`): without branding the package's own primary green applies.
 */
export const DGA_BRANDING = new InjectionToken<DgaBranding>('DGA_BRANDING');

/**
 * Provides a branding to the app: validates it at once (a malformed block fails the bootstrap instead of rendering in
 * the wrong colours), makes it injectable as {@link DGA_BRANDING} and applies its colours to the document when the
 * environment injector starts.
 *
 * @throws DgaBrandingInvalidError when the branding is malformed.
 * @example
 * bootstrapApplication(App, { providers: [provideDgaBranding(config.branding)] });
 */
export function provideDgaBranding(branding: DgaBranding): EnvironmentProviders {
  const valid = parseDgaBranding(branding);
  return makeEnvironmentProviders([
    { provide: DGA_BRANDING, useValue: valid },
    provideEnvironmentInitializer(() => applyDgaBranding(inject(DOCUMENT), valid)),
  ]);
}
