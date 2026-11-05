import type { EnvironmentProviders } from '@angular/core';
import { provideDgaBranding } from '@codelab/ng-dga';
import type { AppConfig } from './app-config';

/**
 * The branding providers for the runtime configuration: `provideDgaBranding` (the `DGA_BRANDING` token plus the
 * `--dga-brand-*` colours on `<html>`) when `config.json` has a branding block, nothing otherwise (the DGA default
 * green). Spread it into the application providers next to `APP_CONFIG`.
 */
export function provideAppBranding(config: AppConfig): EnvironmentProviders[] {
  return config.branding === undefined ? [] : [provideDgaBranding(config.branding)];
}
