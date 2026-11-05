import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import {
  DgaAccordionComponent,
  DgaAccordionHeadingLevel,
  DgaAccordionItemComponent,
  DgaAccordionMode,
} from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ShowcaseDemoComponent } from '../frame/showcase-demo.component';
import { ShowcaseSectionComponent } from '../frame/showcase-section.component';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';

/** Gallery sections of the content components: dga-accordion and a stand-alone dga-accordion-item. */
@Component({
  selector: 'app-showcase-content-demos',
  imports: [
    DgaAccordionComponent,
    DgaAccordionItemComponent,
    ShowcaseDemoComponent,
    ShowcaseSectionComponent,
    TranslatePipe,
  ],
  templateUrl: './content-demos.component.html',
  styleUrl: './demos.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContentDemosComponent {
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly Mode = DgaAccordionMode;
  protected readonly HeadingLevel = DgaAccordionHeadingLevel;

  readonly firstOpen = signal(true);
  readonly standaloneOpen = signal(false);
}
