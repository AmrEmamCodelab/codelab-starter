import { ChangeDetectionStrategy, Component, booleanAttribute, computed, inject, input } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { DgaIconComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService, type TranslationParams } from '../../core/i18n/translation.service';
import { formatKpiValue, hasKpiValue, uniqueTiles, type KpiTile, type QuickAction } from './kpi-tiles';
import { LandingDashboardTranslationKeys } from './landing-dashboard-translation-keys';

/**
 * The landing dashboard page pattern: the first page a signed-in user sees. A greeting or hero, a row of KPI tiles,
 * quick actions and the user's recent items. It renders state only: the screen loads the figures, builds the links
 * from the route constants and filters tiles and actions by permission before binding them.
 *
 * Inputs: `greetingKey` + `greetingParams` (a plain `h1`; leave null when the `[landingHero]` slot carries the
 * heading), `tiles` ({@link KpiTile}[]), `quickActions` ({@link QuickAction}[]; none hides the section), `loading`
 * (tiles show a placeholder), `kpisLabelKey`, `quickActionsLabelKey`, `recentLabelKey` (null hides the recent
 * section).
 *
 * Slots: `[landingHero]` (a `dga-hero` or a welcome card) and `[landingRecent]` (a short list or the work queue's
 * first page).
 */
@Component({
  selector: 'app-landing-dashboard-pattern',
  imports: [DgaIconComponent, NgTemplateOutlet, RouterLink, TranslatePipe],
  templateUrl: './landing-dashboard-pattern.component.html',
  styleUrl: './landing-dashboard-pattern.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingDashboardPatternComponent {
  readonly greetingKey = input<string | null>(null);
  readonly greetingParams = input<TranslationParams | undefined>(undefined);
  readonly tiles = input<readonly KpiTile[]>([]);
  readonly quickActions = input<readonly QuickAction[]>([]);
  readonly loading = input(false, { transform: booleanAttribute });
  readonly kpisLabelKey = input<string>(LandingDashboardTranslationKeys.Kpis);
  readonly quickActionsLabelKey = input<string>(LandingDashboardTranslationKeys.QuickActions);
  readonly recentLabelKey = input<string | null>(LandingDashboardTranslationKeys.Recent);

  private readonly translations = inject(TranslationService);

  protected readonly keys = LandingDashboardTranslationKeys;
  protected readonly idPrefix = `app-landing-dashboard-${nextLandingId()}`;
  readonly shownTiles = computed(() => uniqueTiles(this.tiles()));

  /** The tile's figure for the active language, or null while it is loading or unknown. */
  valueOf(tile: KpiTile): string | null {
    if (this.loading() || !hasKpiValue(tile.value)) {
      return null;
    }
    return formatKpiValue(tile.value, this.translations.language());
  }
}

let landingCount = 0;

function nextLandingId(): number {
  landingCount += 1;
  return landingCount;
}
