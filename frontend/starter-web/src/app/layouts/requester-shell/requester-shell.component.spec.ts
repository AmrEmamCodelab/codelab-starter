import { signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { provideTestAppConfig } from '../../core/config/app-config.testing';
import { PermissionKeys } from '../../core/permissions/permission-keys.generated';
import { PermissionStore } from '../../core/permissions/permission.store';
import {
  ANY_URL_ROUTES,
  aNotification,
  BOTH_DIRECTIONS,
  fakeAuth,
  fixedClock,
  layoutTestProviders,
  useDirection,
} from '../layout.testing';
import { NOTIFICATIONS_SOURCE, type NotificationItem, type NotificationsSource } from '../notifications-menu/notifications';
import { LayoutChromeTranslationKeys as Keys } from '../portal-shell/layout-chrome-translation-keys';
import { REQUESTER_SHELL_OPTIONS, type RequesterShellOptions } from './requester-nav';
import { REQUESTER_MAIN_ID, RequesterShellComponent } from './requester-shell.component';

const OPTIONS: RequesterShellOptions = {
  homeLink: '/',
  notificationsLink: '/notifications',
  navItems: [
    { id: 'faq', labelKey: 'nav.faq', link: '/portal/faq' },
    { id: 'users', labelKey: 'nav.users', link: '/admin/users', permission: PermissionKeys.UsersView },
  ],
};

function fakeSource(items: readonly NotificationItem[]): NotificationsSource {
  return {
    items: signal(items),
    hasMore: signal(false),
    markRead: vi.fn<(id: string) => Promise<void>>().mockResolvedValue(undefined),
    loadMore: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
  };
}

describe.each(BOTH_DIRECTIONS)('RequesterShellComponent (%s)', direction => {
  let restoreDirection: () => void;
  let fixture: ComponentFixture<RequesterShellComponent>;
  let host: HTMLElement;
  let source: NotificationsSource;
  let store: PermissionStore;

  beforeEach(() => {
    restoreDirection = useDirection(direction);
    source = fakeSource([aNotification({ id: 'n-1', read: false })]);
    TestBed.configureTestingModule({
      imports: [RequesterShellComponent],
      providers: [
        provideRouter(ANY_URL_ROUTES),
        ...layoutTestProviders(fakeAuth()),
        fixedClock(),
        provideTestAppConfig(),
        { provide: REQUESTER_SHELL_OPTIONS, useValue: OPTIONS },
        { provide: NOTIFICATIONS_SOURCE, useValue: source },
      ],
    });
    store = TestBed.inject(PermissionStore);
  });

  afterEach(() => {
    host.remove();
    restoreDirection();
  });

  function render(): void {
    fixture = TestBed.createComponent(RequesterShellComponent);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);
  }

  const navLabels = () =>
    [...host.querySelectorAll('.dga-header__nav a.dga-header__link > span')].map(label => label.textContent?.trim());

  it('frames the page: stamp strip, header, main outlet and footer', () => {
    render();
    const parts = [...host.querySelectorAll('dga-stamp-strip, dga-header, main, app-footer')].map(part =>
      part.tagName.toLowerCase(),
    );
    expect(parts).toEqual(['dga-stamp-strip', 'dga-header', 'main', 'app-footer']);
    expect(host.querySelector('main')?.id).toBe(REQUESTER_MAIN_ID);
    expect(host.querySelector('main router-outlet')).not.toBeNull();
  });

  it('puts the bell, the language switcher and the user menu in the header actions', () => {
    render();
    const actions = host.querySelector('[dgaHeaderActions]');
    expect(actions?.querySelector('app-notifications-menu')).not.toBeNull();
    expect(actions?.querySelector('app-language-switcher')).not.toBeNull();
    expect(actions?.querySelector('app-user-profile-menu')).not.toBeNull();
  });

  it('shows only the navigation the user holds keys for, and follows a grant without a reload', () => {
    store.set([]);
    render();
    expect(navLabels()).toEqual(['nav.faq']);
    store.set([PermissionKeys.UsersView]);
    fixture.detectChanges();
    expect(navLabels()).toEqual(['nav.faq', 'nav.users']);
  });

  it('feeds the bell from the notifications source and sends mark-read back to it', () => {
    render();
    const bell = host.querySelector('.notifications__bell') as HTMLButtonElement;
    expect(bell.getAttribute('aria-label')).toContain('count=1');
    bell.click();
    fixture.detectChanges();
    expect(host.querySelector('.notifications__view-all')?.getAttribute('href')).toBe('/notifications');
    (host.querySelector('.notifications__mark') as HTMLButtonElement).click();
    expect(source.markRead).toHaveBeenCalledWith('n-1');
  });

  it('moves focus onto the page from the skip link', () => {
    render();
    const skip = host.querySelector('.frame__skip') as HTMLAnchorElement;
    expect(skip.textContent?.trim()).toBe(Keys.SkipLink);
    skip.click();
    expect(document.activeElement).toBe(host.querySelector('main'));
  });
});
