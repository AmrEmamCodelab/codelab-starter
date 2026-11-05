import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  DgaBreadcrumbComponent,
  DgaButtonComponent,
  DgaHeaderComponent,
  DgaStampStripComponent,
  DgaStepperComponent,
  DgaStepperOrientation,
  DgaTabsComponent,
  dgaTabPanelId,
  type DgaStep,
  type DgaTabValue,
} from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { ShowcaseDemoComponent } from '../frame/showcase-demo.component';
import { ShowcaseSectionComponent } from '../frame/showcase-section.component';
import { ShowcasePageId, showcaseLink } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import {
  BREADCRUMB_FIXTURE,
  NAV_FIXTURE,
  STEP_FIXTURE,
  STEP_WITH_HINT,
  TAB_FIXTURE,
  TAB_VALUES,
  navItems,
  withLabels,
} from './component-fixtures';

/** The panel id prefix of the tabs demo (tabs point at their panels through aria-controls). */
export const TABS_PANEL_PREFIX = 'showcase-tabs';

/** The step the vertical stepper demo marks invalid. */
const INVALID_STEP = 2;

/** Gallery sections of the navigation components: breadcrumb, header, stamp strip, stepper and tabs. */
@Component({
  selector: 'app-showcase-navigation-demos',
  imports: [
    DgaBreadcrumbComponent,
    DgaButtonComponent,
    DgaHeaderComponent,
    DgaStampStripComponent,
    DgaStepperComponent,
    DgaTabsComponent,
    ShowcaseDemoComponent,
    ShowcaseSectionComponent,
    TranslatePipe,
  ],
  templateUrl: './navigation-demos.component.html',
  styleUrl: './demos.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NavigationDemosComponent {
  private readonly translations = inject(TranslationService);
  private readonly translate = (key: string) => this.translations.translate(key);

  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly Orientation = DgaStepperOrientation;
  protected readonly homeLink = showcaseLink(ShowcasePageId.Components);
  protected readonly panelPrefix = TABS_PANEL_PREFIX;

  protected readonly breadcrumbs = computed(() => withLabels(BREADCRUMB_FIXTURE, this.translate));
  protected readonly navItems = computed(() => navItems(NAV_FIXTURE, this.translate));
  protected readonly steps = computed<DgaStep[]>(() => withLabels(STEP_FIXTURE, this.translate));
  protected readonly verticalSteps = computed<DgaStep[]>(() =>
    this.steps().map((step, index) => ({
      ...step,
      description: index === STEP_WITH_HINT ? this.translate(ShowcaseTranslationKeys.Demo.StepRequestHint) : undefined,
      invalid: index === INVALID_STEP,
    })),
  );
  protected readonly tabs = computed(() => withLabels(TAB_FIXTURE, this.translate));

  readonly activeStep = signal(1);
  readonly verticalStep = signal(1);
  readonly tab = signal<DgaTabValue>(TAB_VALUES.Open);
  readonly stampExpanded = signal(false);

  protected panelId(value: DgaTabValue): string {
    return dgaTabPanelId(TABS_PANEL_PREFIX, value);
  }
}
