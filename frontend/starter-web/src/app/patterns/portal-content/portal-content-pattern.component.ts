import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, linkedSignal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PortalContentTranslationKeys } from './portal-content-translation-keys';
import { MIN_TOC_ENTRIES, activeSection, buildToc, tocHref, type PortalSection, type TocEntry } from './table-of-contents';

/** How far below the viewport top a section counts as the one being read (pixels; clears a sticky header). */
export const ACTIVE_SECTION_OFFSET_PX = 96;

const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)';

/**
 * The portal content page pattern: the frame of a public information page (service guide, FAQ, privacy, terms). A
 * page header, a table of contents built from the screen's section list with in-page anchors, the sections, and a
 * closing "last updated" line. The text is the screen's: static bilingual i18n now, settings data later.
 *
 * - Header: project `dga-portal-page-header` (or any header) with `[portalHeader]`, or set `titleKey` (and
 *   `subtitleKey`) for a plain heading. Use one or the other.
 * - Sections: the default slot. Mark each section element with `[portalSection]="id"` (the
 *   `PortalSectionDirective`) using the ids of `sections`; any content fits inside (cards, `dga-accordion`...).
 * - `[portalLastUpdated]`: the "last updated" line and other closing content (page feedback).
 *
 * The table of contents shows from {@link MIN_TOC_ENTRIES} sections. Following an entry scrolls to the section (no
 * animation when the reader asked for reduced motion) and moves focus to it; the entry of the section in view is
 * marked `aria-current="location"`.
 */
@Component({
  selector: 'app-portal-content-pattern',
  imports: [TranslatePipe],
  templateUrl: './portal-content-pattern.component.html',
  styleUrl: './portal-content-pattern.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(window:scroll)': 'onScroll()',
  },
})
export class PortalContentPatternComponent {
  readonly titleKey = input<string | null>(null);
  readonly subtitleKey = input<string | null>(null);
  readonly sections = input<readonly PortalSection[]>([]);
  readonly tocLabelKey = input<string>(PortalContentTranslationKeys.Toc);

  private readonly document = inject(DOCUMENT);
  private scrollFrame: number | null = null;

  protected readonly tocHeadingId = `app-portal-content-toc-${nextPortalContentId()}`;
  readonly toc = computed(() => buildToc(this.sections()));
  readonly showToc = computed(() => this.toc().length >= MIN_TOC_ENTRIES);
  /** The anchor id of the section being read; follows the first section until the reader scrolls or follows a link. */
  readonly active = linkedSignal<string | null>(() => this.toc().at(0)?.anchorId ?? null);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.cancelFrame());
  }

  hrefOf(entry: TocEntry): string {
    const location = this.document.location;
    return tocHref(`${location.pathname}${location.search}`, entry.anchorId);
  }

  /** Follows an entry inside the page: the router never sees the fragment, so the app does not navigate. */
  follow(event: Event, entry: TocEntry): void {
    const section = this.document.getElementById(entry.anchorId);
    if (!section) {
      return;
    }
    event.preventDefault();
    this.active.set(entry.anchorId);
    if (typeof section.scrollIntoView === 'function') {
      section.scrollIntoView({ behavior: this.prefersReducedMotion() ? 'auto' : 'smooth', block: 'start' });
    }
    section.focus({ preventScroll: true });
  }

  onScroll(): void {
    const view = this.document.defaultView;
    if (!view || this.scrollFrame !== null || !this.showToc()) {
      return;
    }
    this.scrollFrame = view.requestAnimationFrame(() => {
      this.scrollFrame = null;
      this.active.set(activeSection(this.toc(), this.sectionTops(), ACTIVE_SECTION_OFFSET_PX));
    });
  }

  private sectionTops(): ReadonlyMap<string, number> {
    const tops = new Map<string, number>();
    for (const entry of this.toc()) {
      const section = this.document.getElementById(entry.anchorId);
      if (section) {
        tops.set(entry.anchorId, section.getBoundingClientRect().top);
      }
    }
    return tops;
  }

  private prefersReducedMotion(): boolean {
    return this.document.defaultView?.matchMedia?.(REDUCED_MOTION_QUERY).matches ?? false;
  }

  private cancelFrame(): void {
    if (this.scrollFrame !== null) {
      this.document.defaultView?.cancelAnimationFrame(this.scrollFrame);
      this.scrollFrame = null;
    }
  }
}

let portalContentCount = 0;

function nextPortalContentId(): number {
  portalContentCount += 1;
  return portalContentCount;
}
