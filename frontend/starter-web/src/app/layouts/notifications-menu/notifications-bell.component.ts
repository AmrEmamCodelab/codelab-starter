import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { NOTIFICATIONS_FEED_LINK, NOTIFICATIONS_SOURCE } from './notifications';
import { NotificationsMenuComponent } from './notifications-menu.component';

/**
 * The header bell wired to the app's notifications: renders {@link NotificationsMenuComponent} over the injected
 * `NOTIFICATIONS_SOURCE` (items, and the server's unread total when the source has one) with the "view all" link from
 * `NOTIFICATIONS_FEED_LINK`, and forwards mark-read to the source. Input-free, so a shell only places it.
 */
@Component({
  selector: 'app-notifications-bell',
  imports: [NotificationsMenuComponent],
  templateUrl: './notifications-bell.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationsBellComponent {
  private readonly source = inject(NOTIFICATIONS_SOURCE);

  readonly items = this.source.items;
  readonly feedLink = inject(NOTIFICATIONS_FEED_LINK);
  readonly unreadTotal = computed(() => this.source.unreadCount?.() ?? null);

  markRead(id: string): void {
    void this.source.markRead(id);
  }
}
