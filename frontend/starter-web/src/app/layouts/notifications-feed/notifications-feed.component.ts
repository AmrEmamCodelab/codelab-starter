import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaButtonComponent, DgaIconComponent } from '@codelab/ng-dga';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import {
  filterNotifications,
  formatNotificationTime,
  NotificationFilter,
  type NotificationItem,
} from '../notifications-menu/notifications';
import { NotificationsTranslationKeys } from '../notifications-menu/notifications-translation-keys';

/** One filter button of the feed: the filter it applies and its label. */
export interface NotificationFilterOption {
  readonly filter: NotificationFilter;
  readonly labelKey: string;
}

/** The feed's filter buttons, in display order. */
export const NOTIFICATION_FILTER_OPTIONS: readonly NotificationFilterOption[] = [
  { filter: NotificationFilter.All, labelKey: NotificationsTranslationKeys.FilterAll },
  { filter: NotificationFilter.Unread, labelKey: NotificationsTranslationKeys.FilterUnread },
  { filter: NotificationFilter.Read, labelKey: NotificationsTranslationKeys.FilterRead },
];

/** The empty-state text for each filter (shown when the active filter keeps nothing). */
export const NotificationEmptyKeys: Readonly<Record<NotificationFilter, string>> = {
  [NotificationFilter.All]: NotificationsTranslationKeys.Empty,
  [NotificationFilter.Unread]: NotificationsTranslationKeys.EmptyUnread,
  [NotificationFilter.Read]: NotificationsTranslationKeys.EmptyRead,
};

/**
 * The full notifications list for a feed page: a read/unread filter (toggle buttons with `aria-pressed`), the list or
 * an empty state for the active filter, a mark-read action per unread notification and a "load more" button while
 * older ones remain. Pure presentation: the page passes the `NotificationsSource` state in (`items`, `hasMore`,
 * `loading`) and wires `markRead` and `loadMore` back to it. The count shown is announced politely when it changes.
 */
@Component({
  selector: 'app-notifications-feed',
  imports: [RouterLink, DgaButtonComponent, DgaIconComponent, TranslatePipe],
  templateUrl: './notifications-feed.component.html',
  styleUrl: './notifications-feed.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationsFeedComponent {
  private readonly language = inject(LanguageService);

  /** Every notification loaded so far, newest first. */
  readonly items = input<readonly NotificationItem[]>([]);
  /** True while older notifications remain to load. */
  readonly hasMore = input(false);
  /** True while the next page is loading (the button shows its busy state). */
  readonly loading = input(false);
  readonly markRead = output<string>();
  readonly loadMore = output();

  protected readonly keys = NotificationsTranslationKeys;
  protected readonly filters = NOTIFICATION_FILTER_OPTIONS;
  readonly filter = signal(NotificationFilter.All);
  readonly visible = computed(() => filterNotifications(this.items(), this.filter()));
  readonly emptyKey = computed(() => NotificationEmptyKeys[this.filter()]);

  time(at: string): string {
    return formatNotificationTime(at, this.language.current());
  }

  select(filter: NotificationFilter): void {
    this.filter.set(filter);
  }
}
