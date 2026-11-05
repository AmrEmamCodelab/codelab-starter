import { DOCUMENT } from '@angular/common';
import { Injectable, OnDestroy, inject, signal } from '@angular/core';
import { DgaBrandProperty, applyDgaBranding, type DgaBranding } from '@codelab/ng-dga';
import { AppConfigService } from '../../core/config/app-config.service';

/** Sample primary colours the branding page lets a developer try (data: hex values, not wording). */
export const SAMPLE_BRAND_COLOURS: readonly string[] = ['#0a5c8f', '#7a3e9d', '#b4541a'];

/** The name the preview uses when the app has no branding block (only the colour matters for a preview). */
const PREVIEW_NAME = { ar: '-', en: '-' } as const;

/**
 * Tries a primary colour on the whole page through ng-dga's own `applyDgaBranding` (the `--dga-brand-*` properties on
 * the root element), and puts the configured branding back on `reset()` and when the page is left, so a preview never
 * outlives the branding page. Provided by the branding page itself.
 */
@Injectable()
export class BrandingPreview implements OnDestroy {
  private readonly document = inject(DOCUMENT);
  private readonly configured = inject(AppConfigService).branding;

  /** The colour being previewed, or null while the configured branding shows. */
  readonly trying = signal<string | null>(null);

  /** Previews `primaryColor` on the page. @throws DgaBrandingInvalidError for a colour that is not `#rrggbb`. */
  tryColour(primaryColor: string): void {
    const base: DgaBranding = this.configured ?? { primaryColor, logoSrc: '', appName: PREVIEW_NAME };
    applyDgaBranding(this.document, { ...base, primaryColor });
    this.trying.set(primaryColor);
  }

  /** Back to the configured branding (or to the package default when there is none). */
  reset(): void {
    if (this.trying() === null) {
      return;
    }
    if (this.configured === undefined) {
      this.clearBrandProperties();
    } else {
      applyDgaBranding(this.document, this.configured);
    }
    this.trying.set(null);
  }

  ngOnDestroy(): void {
    this.reset();
  }

  private clearBrandProperties(): void {
    const style = this.document.documentElement.style;
    for (const property of Object.values(DgaBrandProperty)) {
      style.removeProperty(property);
    }
  }
}
