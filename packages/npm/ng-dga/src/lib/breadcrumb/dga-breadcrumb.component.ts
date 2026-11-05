import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { type DgaBreadcrumbItem, resolveBreadcrumbs } from './breadcrumb-items';

/**
 * The DGA breadcrumb: a "where am I" trail (WAI-ARIA breadcrumb pattern). A navigation landmark named by `label`
 * holds an ordered list of crumbs; earlier crumbs with a `link` are router links, and the current page is plain text
 * with `aria-current="page"`. The separator chevron is decorative and points along the reading direction (it
 * mirrors in RTL through the icon manifest), so no language service is needed.
 *
 * @example
 * <dga-breadcrumb [label]="'nav.breadcrumb' | translate"
 *   [items]="[{ label: 'nav.home' | translate, link: '/', icon: 'home' }, { label: 'nav.admin' | translate }]" />
 */
@Component({
  selector: 'dga-breadcrumb',
  imports: [RouterLink, DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-breadcrumb.component.html',
  styleUrl: './dga-breadcrumb.component.css',
})
export class DgaBreadcrumbComponent {
  readonly items = input.required<readonly DgaBreadcrumbItem[]>();
  /** Accessible name of the navigation landmark (already translated). */
  readonly label = input.required<string>();

  readonly resolved = computed(() => resolveBreadcrumbs(this.items()));
}
