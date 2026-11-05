import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { AppFooterComponent } from '../../layouts/app-footer/app-footer.component';
import { NotificationsFeedComponent } from '../../layouts/notifications-feed/notifications-feed.component';
import { NOTIFICATIONS_SOURCE } from '../../layouts/notifications-menu/notifications';
import { NotificationsMenuComponent } from '../../layouts/notifications-menu/notifications-menu.component';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { ShowcaseHeadingLevel, ShowcaseSectionComponent } from '../frame/showcase-section.component';
import { ShowcasePageId, showcaseLink } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { provideShowcaseNotifications } from './showcase-notifications';

/** The anchors of the layouts page sections. */
export const LayoutsSection = {
  Footer: 'app-footer',
  NotificationsMenu: 'app-notifications-menu',
  NotificationsFeed: 'app-notifications-feed',
  Shells: 'shells',
} as const;

/**
 * The layout parts on their own: the notifications bell with its menu and the full notifications feed (both fed by
 * the in-memory showcase source) and the footer. The two shells have their own framed pages (each is a whole page).
 */
@Component({
  selector: 'app-showcase-layouts-page',
  imports: [
    AppFooterComponent,
    NotificationsFeedComponent,
    NotificationsMenuComponent,
    RouterLink,
    ShowcaseFrameComponent,
    ShowcaseSectionComponent,
    TranslatePipe,
  ],
  providers: [provideShowcaseNotifications()],
  templateUrl: './layouts-page.component.html',
  styleUrl: './layouts-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LayoutsPageComponent {
  protected readonly pageId = ShowcasePageId.Layouts;
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly Section = LayoutsSection;
  protected readonly Level = ShowcaseHeadingLevel;
  protected readonly notifications = inject(NOTIFICATIONS_SOURCE);
  protected readonly feedLink = showcaseLink(ShowcasePageId.Layouts);
  protected readonly portalShellLink = showcaseLink(ShowcasePageId.PortalShell);
  protected readonly requesterShellLink = showcaseLink(ShowcasePageId.RequesterShell);

  markRead(id: string): Promise<void> {
    return this.notifications.markRead(id);
  }

  loadMore(): Promise<void> {
    return this.notifications.loadMore();
  }
}
