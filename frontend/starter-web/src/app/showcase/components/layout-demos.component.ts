import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import {
  DgaButtonComponent,
  DgaCardComponent,
  DgaHeroComponent,
  DgaHeroVariant,
  DgaPortalPageHeaderComponent,
} from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { ShowcaseDemoComponent } from '../frame/showcase-demo.component';
import { ShowcaseSectionComponent } from '../frame/showcase-section.component';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { BREADCRUMB_FIXTURE, withLabels } from './component-fixtures';

/** Gallery sections of the layout components: dga-card, dga-hero, dga-portal-page-header. */
@Component({
  selector: 'app-showcase-layout-demos',
  imports: [
    DgaButtonComponent,
    DgaCardComponent,
    DgaHeroComponent,
    DgaPortalPageHeaderComponent,
    ShowcaseDemoComponent,
    ShowcaseSectionComponent,
    TranslatePipe,
  ],
  templateUrl: './layout-demos.component.html',
  styleUrl: './demos.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LayoutDemosComponent {
  private readonly translations = inject(TranslationService);

  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly HeroVariant = DgaHeroVariant;
  protected readonly breadcrumbs = computed(() => withLabels(BREADCRUMB_FIXTURE, key => this.translations.translate(key)));
}
