import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { PermissionStore } from '../../../core/permissions/permission.store';
import { DgaPageFeedbackKind } from '@codelab/ng-dga';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ANY_URL_ROUTES,
  aNotification,
  BOTH_DIRECTIONS,
  fakeAuth,
  layoutTestProviders,
  useDirection,
} from '../../../layouts/layout.testing';
import type { NotificationItem } from '../../../layouts/notifications-menu/notifications';
import { ApiNotificationsSource, NotificationsLoadStatus } from '../data/api-notifications.source';
import { NotificationsClient } from '../data/notifications.client';
import { NotificationsPageTranslationKeys as Keys } from '../notifications-translation-keys';
import { NotificationsPageComponent, notificationsFeedback } from './notifications-page.component';

function fakeSource(status: NotificationsLoadStatus, items: readonly NotificationItem[] = []) {
  return {
    status: signal(status),
    items: signal(items),
    hasMore: signal(true),
    loadingMore: signal(false),
    refresh: vi.fn().mockResolvedValue(undefined),
    markRead: vi.fn().mockResolvedValue(undefined),
    loadMore: vi.fn().mockResolvedValue(undefined),
  };
}

type FakeSource = ReturnType<typeof fakeSource>;

const SEND_TEST_SELECTOR = '[data-testid="notifications-send-test"]';

function fakeClient(fails = false) {
  return { sendTest: vi.fn(() => (fails ? throwError(() => new Error('refused')) : of(undefined))) };
}

function render(source: FakeSource, heldKeys: readonly string[] = [], client = fakeClient()): HTMLElement {
  TestBed.configureTestingModule({
    imports: [NotificationsPageComponent],
    providers: [
      provideRouter(ANY_URL_ROUTES),
      ...layoutTestProviders(fakeAuth()),
      { provide: ApiNotificationsSource, useValue: source },
      { provide: NotificationsClient, useValue: client },
      { provide: PermissionStore, useValue: { has: (key: string) => heldKeys.includes(key) } },
    ],
  });
  const fixture = TestBed.createComponent(NotificationsPageComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const echo = (key: string): string => key;

describe('notificationsFeedback', () => {
  it('shows loading as information, a failure as an error, and nothing otherwise', () => {
    expect(notificationsFeedback(NotificationsLoadStatus.Loading, echo)).toEqual({ kind: DgaPageFeedbackKind.Info, message: Keys.Loading });
    expect(notificationsFeedback(NotificationsLoadStatus.Failed, echo)).toEqual({
      kind: DgaPageFeedbackKind.Error,
      title: Keys.FailedTitle,
      message: Keys.FailedBody,
    });
    expect(notificationsFeedback(NotificationsLoadStatus.Loaded, echo)).toBeNull();
    expect(notificationsFeedback(NotificationsLoadStatus.SignedOut, echo)).toBeNull();
  });
});

describe.each(BOTH_DIRECTIONS)('NotificationsPageComponent (%s)', direction => {
  let restore: () => void = () => undefined;
  afterEach(() => restore());

  it('loads the newest notifications on open and lists them in the feed', () => {
    restore = useDirection(direction);
    const source = fakeSource(NotificationsLoadStatus.Loaded, [aNotification({ id: 'n-1', titleText: 'Hello' })]);
    const host = render(source);

    expect(source.refresh).toHaveBeenCalledTimes(1);
    expect(host.querySelectorAll('h1')).toHaveLength(1);
    expect(host.querySelector('h1')?.textContent?.trim()).toBe(Keys.Title);
    expect(host.querySelector('.feed__item-title')?.textContent?.trim()).toBe('Hello');
    expect(host.querySelector('.dga-page-feedback')).toBeNull();
  });

  it('forwards mark-read and load-more to the source', () => {
    restore = useDirection(direction);
    const source = fakeSource(NotificationsLoadStatus.Loaded, [aNotification({ id: 'n-7' })]);
    const host = render(source);

    host.querySelector<HTMLElement>('.feed__item-foot dga-button')?.click();
    host.querySelector<HTMLElement>('.feed__more dga-button')?.click();

    expect(source.markRead).toHaveBeenCalledWith('n-7');
    expect(source.loadMore).toHaveBeenCalledTimes(1);
  });

  it('shows the loading state without the feed', () => {
    restore = useDirection(direction);
    const host = render(fakeSource(NotificationsLoadStatus.Loading));
    expect(host.querySelector('.dga-page-feedback__message')?.textContent).toBe(Keys.Loading);
    expect(host.querySelector('app-notifications-feed')).toBeNull();
  });

  it('shows the failure with a retry that loads again', () => {
    restore = useDirection(direction);
    const source = fakeSource(NotificationsLoadStatus.Failed);
    const host = render(source);

    expect(host.querySelector('.dga-page-feedback')?.getAttribute('role')).toBe('alert');
    const retry = host.querySelector<HTMLElement>('[data-testid="notifications-retry"]');
    expect(retry?.hasAttribute('data-public-action')).toBe(true);
    retry?.click();
    expect(source.refresh).toHaveBeenCalledTimes(2);
  });

  it('offers the test send only to a notifications.test holder, and reloads the feed once it is accepted', () => {
    restore = useDirection(direction);
    expect(render(fakeSource(NotificationsLoadStatus.Loaded)).querySelector(SEND_TEST_SELECTOR)).toBeNull();
    TestBed.resetTestingModule();

    const source = fakeSource(NotificationsLoadStatus.Loaded);
    const client = fakeClient();
    const host = render(source, [PermissionKeys.NotificationsTest], client);
    host.querySelector<HTMLElement>(SEND_TEST_SELECTOR)?.click();
    expect(client.sendTest).toHaveBeenCalledTimes(1);
    expect(source.refresh).toHaveBeenCalledTimes(2);
  });

  it('leaves the feed alone when the test send is refused', () => {
    restore = useDirection(direction);
    const source = fakeSource(NotificationsLoadStatus.Loaded);
    const host = render(source, [PermissionKeys.NotificationsTest], fakeClient(true));
    host.querySelector<HTMLElement>(SEND_TEST_SELECTOR)?.click();
    expect(source.refresh).toHaveBeenCalledTimes(1);
  });

  it('shows the feed empty state once loaded without notifications', () => {
    restore = useDirection(direction);
    const host = render(fakeSource(NotificationsLoadStatus.Loaded));
    expect(host.querySelector('.feed__empty')).not.toBeNull();
  });
});
