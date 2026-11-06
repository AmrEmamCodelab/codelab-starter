import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError, type Observable } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { SKIP_ERROR_DIALOG } from '../../../core/http/http-context';
import { AppLanguages } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { fakeAuth, fakeLanguage, type FakeAuth, type FakeLanguage } from '../../../layouts/layout.testing';
import { ApiNotificationsSource, NotificationsLoadStatus } from './api-notifications.source';
import { UNREAD_POLL_INTERVAL_MS } from './notifications-feed';
import {
  NotificationsClient,
  type NotificationDto,
  type NotificationPageDto,
  type NotificationPageQuery,
  type UnreadCountDto,
} from './notifications.client';

function dto(id: string, read = false): NotificationDto {
  return { id, titleText: `Title ${id}`, bodyText: 'Body', at: '2026-11-05T09:30:00Z', read, link: null };
}

const pageOf = (ids: readonly string[], hasMore: boolean): NotificationPageDto => ({ items: ids.map(id => dto(id)), hasMore });
const refusal = () => throwError(() => new HttpErrorResponse({ status: HttpStatusCode.InternalServerError }));

interface FakeClient {
  readonly page: Mock<(query: NotificationPageQuery) => Observable<NotificationPageDto>>;
  readonly unreadCount: Mock<() => Observable<UnreadCountDto>>;
  readonly markRead: Mock<(id: string) => Observable<void>>;
}

describe('ApiNotificationsSource', () => {
  let client: FakeClient;
  let auth: FakeAuth;
  let language: FakeLanguage;

  beforeEach(() => {
    vi.useFakeTimers();
    client = {
      page: vi.fn((query: NotificationPageQuery) => of(query.before === null ? pageOf(['n-3', 'n-2'], true) : pageOf(['n-1'], false))),
      unreadCount: vi.fn(() => of({ count: 2 })),
      markRead: vi.fn(() => of(undefined)),
    };
    auth = fakeAuth();
    language = fakeLanguage();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  async function create(): Promise<ApiNotificationsSource> {
    TestBed.configureTestingModule({
      providers: [
        { provide: NotificationsClient, useValue: client },
        { provide: AuthService, useValue: auth },
        { provide: LanguageService, useValue: language },
      ],
    });
    const source = TestBed.inject(ApiNotificationsSource);
    await settle();
    return source;
  }

  async function settle(): Promise<void> {
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(0);
  }

  it('loads the newest page and the unread total once signed in, quietly, in the reader language', async () => {
    const source = await create();

    expect(source.status()).toBe(NotificationsLoadStatus.Loaded);
    expect(source.items().map(item => item.id)).toEqual(['n-3', 'n-2']);
    expect(source.hasMore()).toBe(true);
    expect(source.unreadCount()).toBe(2);
    const [query] = client.page.mock.calls[0] ?? [];
    expect(query).toMatchObject({ before: null, language: AppLanguages.Arabic });
    // No page size: the server's notifications.page-size setting decides, so an administrator's change takes effect.
    expect(query).not.toHaveProperty('take');
    expect(query?.context?.get(SKIP_ERROR_DIALOG)).toBe(true);
  });

  it('loads nothing and polls nothing while signed out', async () => {
    auth.isAuthenticated.set(false);
    const source = await create();

    await vi.advanceTimersByTimeAsync(UNREAD_POLL_INTERVAL_MS * 2);
    expect(source.status()).toBe(NotificationsLoadStatus.SignedOut);
    expect(client.page).not.toHaveBeenCalled();
    expect(client.unreadCount).not.toHaveBeenCalled();
    await source.refresh();
    expect(client.page).not.toHaveBeenCalled();
  });

  it('clears everything and stops polling on sign-out', async () => {
    const source = await create();
    auth.isAuthenticated.set(false);
    await settle();

    expect(source.items()).toEqual([]);
    expect(source.unreadCount()).toBe(0);
    expect(source.status()).toBe(NotificationsLoadStatus.SignedOut);
    const calls = client.unreadCount.mock.calls.length;
    await vi.advanceTimersByTimeAsync(UNREAD_POLL_INTERVAL_MS * 2);
    expect(client.unreadCount.mock.calls.length).toBe(calls);
  });

  it('reloads in the new language when the reader switches', async () => {
    await create();
    language.current.set(AppLanguages.English);
    await settle();
    expect(client.page.mock.calls.at(-1)?.[0].language).toBe(AppLanguages.English);
  });

  it('polls the unread count and merges the newest page in when it moved', async () => {
    const source = await create();
    client.unreadCount.mockReturnValue(of({ count: 3 }));
    client.page.mockReturnValue(of(pageOf(['n-4', 'n-3', 'n-2'], true)));

    await vi.advanceTimersByTimeAsync(UNREAD_POLL_INTERVAL_MS);

    expect(source.unreadCount()).toBe(3);
    expect(source.items().map(item => item.id)).toEqual(['n-4', 'n-3', 'n-2']);
  });

  it('does not refetch the page when the count is unchanged, and survives a failed tick', async () => {
    await create();
    const pages = client.page.mock.calls.length;
    await vi.advanceTimersByTimeAsync(UNREAD_POLL_INTERVAL_MS);
    expect(client.page.mock.calls.length).toBe(pages);

    client.unreadCount.mockReturnValue(refusal());
    await expect(vi.advanceTimersByTimeAsync(UNREAD_POLL_INTERVAL_MS)).resolves.not.toThrow();
  });

  it('appends older pages through the cursor, and stops when none remain', async () => {
    const source = await create();

    await source.loadMore();

    const [query] = client.page.mock.calls.at(-1) ?? [];
    expect(query?.before).toBe('n-2');
    expect(query?.context).toBeUndefined();
    expect(source.items().map(item => item.id)).toEqual(['n-3', 'n-2', 'n-1']);
    expect(source.hasMore()).toBe(false);
    expect(source.loadingMore()).toBe(false);
    const calls = client.page.mock.calls.length;
    await source.loadMore();
    expect(client.page.mock.calls.length).toBe(calls);
  });

  it('keeps the list when an older page is refused, without rejecting', async () => {
    const source = await create();
    client.page.mockReturnValue(refusal());
    await expect(source.loadMore()).resolves.toBeUndefined();
    expect(source.items().map(item => item.id)).toEqual(['n-3', 'n-2']);
    expect(source.hasMore()).toBe(true);
  });

  it('marks a notification read once the server agreed and lowers the unread total', async () => {
    const source = await create();
    await source.markRead('n-3');

    expect(client.markRead).toHaveBeenCalledWith('n-3');
    expect(source.items().find(item => item.id === 'n-3')?.read).toBe(true);
    expect(source.unreadCount()).toBe(1);

    await source.markRead('n-3');
    await source.markRead('unknown');
    expect(client.markRead).toHaveBeenCalledTimes(1);
  });

  it('leaves a notification unread when mark-read is refused, without rejecting', async () => {
    const source = await create();
    client.markRead.mockReturnValue(refusal());
    await expect(source.markRead('n-3')).resolves.toBeUndefined();
    expect(source.items().find(item => item.id === 'n-3')?.read).toBe(false);
    expect(source.unreadCount()).toBe(2);
  });

  it('reports a failed first page and loads it again on refresh', async () => {
    client.page.mockReturnValueOnce(refusal());
    const source = await create();
    expect(source.status()).toBe(NotificationsLoadStatus.Failed);

    await source.refresh();
    expect(source.status()).toBe(NotificationsLoadStatus.Loaded);
    expect(source.items()).toHaveLength(2);
  });
});
