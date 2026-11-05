import { ViewportScroller } from '@angular/common';
import { ChangeDetectionStrategy, Component, afterNextRender, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { ShowcasePageId } from '../showcase-pages';
import { ShowcaseCategory, componentCategories } from './component-sections';
import { ContentDemosComponent } from './content-demos.component';
import { DataDemosComponent } from './data-demos.component';
import { FeedbackDemosComponent } from './feedback-demos.component';
import { FormFieldDemosComponent } from './form-field-demos.component';
import { FormPickerDemosComponent } from './form-picker-demos.component';
import { LayoutDemosComponent } from './layout-demos.component';
import { MediaDemosComponent } from './media-demos.component';
import { NavigationDemosComponent } from './navigation-demos.component';

/**
 * `/showcase`: every @codelab/ng-dga component, grouped by catalog category, each in a section anchored by its
 * selector (`/showcase#dga-button`, the catalog's live-example links). The page renders lazily after navigation, so
 * it scrolls to the URL fragment itself once the sections exist.
 */
@Component({
  selector: 'app-showcase-components-page',
  imports: [
    ContentDemosComponent,
    DataDemosComponent,
    FeedbackDemosComponent,
    FormFieldDemosComponent,
    FormPickerDemosComponent,
    LayoutDemosComponent,
    MediaDemosComponent,
    NavigationDemosComponent,
    ShowcaseFrameComponent,
    TranslatePipe,
  ],
  templateUrl: './components-page.component.html',
  styleUrl: './components-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ComponentsPageComponent {
  protected readonly pageId = ShowcasePageId.Components;
  protected readonly categories = componentCategories();
  protected readonly Category = ShowcaseCategory;

  constructor() {
    const fragment = inject(ActivatedRoute).snapshot.fragment;
    const scroller = inject(ViewportScroller);
    afterNextRender(() => {
      if (fragment) {
        scroller.scrollToAnchor(fragment);
      }
    });
  }
}
