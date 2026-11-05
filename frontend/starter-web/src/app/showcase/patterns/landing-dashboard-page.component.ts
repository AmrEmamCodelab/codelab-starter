import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DgaStatusTagComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { LandingDashboardPatternComponent } from '../../patterns';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { ShowcaseState } from '../frame/showcase-state';
import { ShowcaseStateSwitchComponent } from '../frame/showcase-state-switch.component';
import { ShowcasePageId } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { QUICK_ACTIONS, RECENT_FIXTURE, kpiTiles } from './pattern-fixtures';

/** The states the landing demo offers: its tiles load, but it has no empty or error state of its own. */
export const LANDING_STATES: readonly ShowcaseState[] = [ShowcaseState.Ready, ShowcaseState.Loading];

/** The landing dashboard pattern: greeting, KPI tiles (two linked), quick actions and recent requests. */
@Component({
  selector: 'app-showcase-landing-dashboard-page',
  imports: [
    DgaStatusTagComponent,
    LandingDashboardPatternComponent,
    ShowcaseFrameComponent,
    ShowcaseStateSwitchComponent,
    TranslatePipe,
  ],
  templateUrl: './landing-dashboard-page.component.html',
  styleUrl: './pattern-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingDashboardPageComponent {
  private readonly translations = inject(TranslationService);

  protected readonly pageId = ShowcasePageId.LandingDashboard;
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly states = LANDING_STATES;
  protected readonly quickActions = QUICK_ACTIONS;
  protected readonly recent = RECENT_FIXTURE;

  readonly state = signal<ShowcaseState>(ShowcaseState.Ready);

  protected readonly loading = computed(() => this.state() === ShowcaseState.Loading);
  protected readonly greetingParams = computed(() => ({
    name: this.translations.translate(ShowcaseTranslationKeys.Patterns.ApplicantOne),
  }));
  /** The tiles; while loading their figures are unknown (null), which the pattern draws as a placeholder. */
  protected readonly tiles = computed(() => {
    const tiles = kpiTiles(key => this.translations.translate(key));
    return this.loading() ? tiles.map(tile => ({ ...tile, value: null })) : tiles;
  });
}
