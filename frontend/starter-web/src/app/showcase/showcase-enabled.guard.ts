import { inject } from '@angular/core';
import type { CanMatchFn } from '@angular/router';
import { AppConfigService } from '../core/config/app-config.service';

/**
 * Lets the `/showcase` pages match only when `config.json` turns them on (`"showcase": true`, dev and staging). With
 * the flag off or missing (production) no showcase route matches, so the URL falls through to the 404 page and the
 * page bundles are never loaded.
 */
export const showcaseEnabledGuard: CanMatchFn = () => inject(AppConfigService).showcase;
