import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DgaBreadcrumbComponent } from '../breadcrumb/dga-breadcrumb.component';
import type { DgaBreadcrumbItem } from '../breadcrumb/breadcrumb-items';
import { hasText } from '../internal/aria';

/**
 * The DGA portal page header: the title band at the top of a public portal page (service guide, FAQ, contact, legal
 * pages). A `dga-breadcrumb` trail, then the page's `<h1>` title and an optional subtitle, with two optional slots:
 * `[illustration]` (a decorative image or icon at the end side) and `[actions]` (buttons under the title).
 *
 * - The breadcrumb shows only when `breadcrumbs` has items; name its landmark with `breadcrumbLabel`.
 * - The band stretches to its container; the page shell decides whether that is full-bleed.
 * - Every text arrives translated. Mark a decorative illustration `aria-hidden="true"` (or give an image an empty
 *   `alt`).
 *
 * @example
 * <dga-portal-page-header [title]="'portal.faq.title' | t" [subtitle]="'portal.faq.subtitle' | t"
 *   [breadcrumbLabel]="'layouts.breadcrumb' | t" [breadcrumbs]="crumbs()">
 *   <img illustration src="assets/faq.svg" alt="" />
 * </dga-portal-page-header>
 */
@Component({
  selector: 'dga-portal-page-header',
  imports: [DgaBreadcrumbComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-portal-page-header.component.html',
  styleUrl: './dga-portal-page-header.component.css',
})
export class DgaPortalPageHeaderComponent {
  /** The page title (already translated); rendered as the page's only `<h1>`. */
  readonly title = input.required<string>();
  readonly subtitle = input<string | null>(null);
  /** The trail above the title, current page last; empty shows no breadcrumb. */
  readonly breadcrumbs = input<readonly DgaBreadcrumbItem[]>([]);
  /** Accessible name of the breadcrumb landmark (already translated); required when there are breadcrumbs. */
  readonly breadcrumbLabel = input<string>('');

  readonly hasBreadcrumbs = computed(() => this.breadcrumbs().length > 0);
  readonly hasSubtitle = computed(() => hasText(this.subtitle()));
}
