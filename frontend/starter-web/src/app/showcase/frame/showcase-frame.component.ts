import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, booleanAttribute, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaTagComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LanguageSwitcherComponent } from '../../layouts/language-switcher/language-switcher.component';
import {
  ShowcaseGroup,
  ShowcasePageId,
  showcaseLink,
  showcasePage,
  showcasePagesIn,
} from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';

/** One navigation group of the frame: its heading and its page links. */
export interface ShowcaseNavGroup {
  readonly id: ShowcaseGroup;
  readonly titleKey: string;
  readonly pages: readonly { readonly id: ShowcasePageId; readonly titleKey: string; readonly link: string }[];
}

const GROUP_TITLES: Readonly<Record<ShowcaseGroup, string>> = {
  [ShowcaseGroup.Components]: ShowcaseTranslationKeys.Frame.GroupComponents,
  [ShowcaseGroup.Patterns]: ShowcaseTranslationKeys.Frame.GroupPatterns,
  [ShowcaseGroup.Layouts]: ShowcaseTranslationKeys.Frame.GroupLayouts,
  [ShowcaseGroup.Branding]: ShowcaseTranslationKeys.Frame.GroupBranding,
};

/** The showcase navigation: every page, grouped and in registry order. */
export const SHOWCASE_NAV: readonly ShowcaseNavGroup[] = Object.values(ShowcaseGroup).map(group => ({
  id: group,
  titleKey: GROUP_TITLES[group],
  pages: showcasePagesIn(group).map(page => ({ id: page.id, titleKey: page.titleKey, link: showcaseLink(page.id) })),
}));

/** The id of the frame's content region (the page's `<main>`, or the plain wrapper around a framed shell). */
export const SHOWCASE_CONTENT_ID = 'showcase-content';

/**
 * The frame every showcase page renders in: a bar with the language switcher, the page navigation, the page title
 * (`h1`) and intro, then the projected demos. A page that frames a whole shell sets `landmark` to false, because the
 * shell brings its own `<main>`.
 */
@Component({
  selector: 'app-showcase-frame',
  imports: [NgTemplateOutlet, RouterLink, DgaTagComponent, LanguageSwitcherComponent, TranslatePipe],
  templateUrl: './showcase-frame.component.html',
  styleUrl: './showcase-frame.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShowcaseFrameComponent {
  readonly pageId = input.required<ShowcasePageId>();
  readonly landmark = input(true, { transform: booleanAttribute });

  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly nav = SHOWCASE_NAV;
  protected readonly homeLink = showcaseLink(ShowcasePageId.Components);
  protected readonly contentId = SHOWCASE_CONTENT_ID;
  protected readonly page = computed(() => showcasePage(this.pageId()));
}
