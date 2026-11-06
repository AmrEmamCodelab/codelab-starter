import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { DgaButtonComponent, DgaPageFeedbackComponent, DgaPageFeedbackKind, type DgaPageFeedbackState } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { NotificationsFeedComponent } from '../../../layouts/notifications-feed/notifications-feed.component';
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import { ApiNotificationsSource, NotificationsLoadStatus } from '../data/api-notifications.source';
import { NotificationsClient } from '../data/notifications.client';
import { NotificationsPageTranslationKeys as Keys } from '../notifications-translation-keys';

/** The banner for the newest page's status: loading is informational, a failure an error; otherwise none. */
export function notificationsFeedback(
  status: NotificationsLoadStatus,
  translate: (key: string) => string,
): DgaPageFeedbackState | null {
  switch (status) {
    case NotificationsLoadStatus.Loading:
      return { kind: DgaPageFeedbackKind.Info, message: translate(Keys.Loading) };
    case NotificationsLoadStatus.Failed:
      return { kind: DgaPageFeedbackKind.Error, title: translate(Keys.FailedTitle), message: translate(Keys.FailedBody) };
    default:
      return null;
  }
}

/**
 * `/notifications` (any signed-in user, inside the staff shell): the reader's notifications in the layouts' feed
 * (read/unread filter, mark-read, load-more), backed by {@link ApiNotificationsSource}. Opening the page loads the
 * newest notifications again; loading and failure show as page feedback, the failure with a retry. A holder of
 * `notifications.test` can send themself the test notification (bell, e-mail and SMS when enabled); the feed reloads
 * once the server accepts it, and a refusal is reported by the HTTP error interceptor.
 */
@Component({
  selector: 'app-notifications-page',
  imports: [DgaButtonComponent, DgaPageFeedbackComponent, HasPermissionDirective, NotificationsFeedComponent, TranslatePipe],
  templateUrl: './notifications-page.component.html',
  styleUrl: './notifications-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationsPageComponent {
  private readonly translations = inject(TranslationService);
  protected readonly source = inject(ApiNotificationsSource);
  private readonly client = inject(NotificationsClient);

  protected readonly keys = Keys;
  protected readonly sendTestPermission = PermissionKeys.NotificationsTest;
  readonly failed = computed(() => this.source.status() === NotificationsLoadStatus.Failed);
  /** The feed shows once the newest page is in (and stays while a later refresh runs over loaded items). */
  readonly showFeed = computed(
    () => this.source.status() === NotificationsLoadStatus.Loaded || this.source.items().length > 0,
  );
  readonly feedback = computed(() => notificationsFeedback(this.source.status(), key => this.translations.translate(key)));

  constructor() {
    void this.source.refresh();
  }

  markRead(id: string): void {
    void this.source.markRead(id);
  }

  loadMore(): void {
    void this.source.loadMore();
  }

  retry(): void {
    void this.source.refresh();
  }

  sendTest(): void {
    this.client.sendTest().subscribe({
      next: () => void this.source.refresh(),
      error: () => undefined,
    });
  }
}
