import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { DgaButtonComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PatternTranslationKeys } from '../pattern-translation-keys';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE, PAGE_GAP, clampPage, pageCount, pageRange, pagerItems } from './paging';

/**
 * The pager under an admin list: "21–40 of 95", previous/next and numbered pages with gaps. `page` is a 1-based
 * signal model (`[(page)]`); the pager never moves outside 1..pageCount. Used by {@link AdminListPatternComponent};
 * a screen may use it on its own under any paged content.
 *
 * @example
 * <app-list-pager [(page)]="page" [total]="total()" [pageSize]="pageSize" />
 */
@Component({
  selector: 'app-list-pager',
  imports: [DgaButtonComponent, TranslatePipe],
  templateUrl: './list-pager.component.html',
  styleUrl: './list-pager.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ListPagerComponent {
  readonly page = model(FIRST_PAGE);
  readonly total = input(0);
  readonly pageSize = input(DEFAULT_PAGE_SIZE);

  protected readonly keys = PatternTranslationKeys.Pager;
  protected readonly gap = PAGE_GAP;
  readonly count = computed(() => pageCount(this.total(), this.pageSize()));
  readonly current = computed(() => clampPage(this.page(), this.total(), this.pageSize()));
  readonly range = computed(() => pageRange(this.current(), this.total(), this.pageSize()));
  readonly items = computed(() => pagerItems(this.current(), this.count()));
  readonly isFirst = computed(() => this.current() <= FIRST_PAGE);
  readonly isLast = computed(() => this.current() >= this.count());

  goTo(page: number): void {
    const target = clampPage(page, this.total(), this.pageSize());
    if (target !== this.page()) {
      this.page.set(target);
    }
  }

  previous(): void {
    this.goTo(this.current() - 1);
  }

  next(): void {
    this.goTo(this.current() + 1);
  }
}
