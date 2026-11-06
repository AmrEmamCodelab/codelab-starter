import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL, provideTestAppConfig } from '../../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG, quietContext } from '../../../core/http/http-context';
import { AppLanguages } from '../../../core/i18n/language';
import { BROWSER_STORAGE } from '../../../core/session/app-storage';
import { MemoryStorage } from '../../../core/session/memory-storage';
import {
  ACCEPT_LANGUAGE_HEADER,
  NotificationsApiPaths,
  NotificationsClient,
  NotificationsQueryParams,
  notificationReadPath,
  type NotificationPageDto,
} from './notifications.client';

const PAGE: NotificationPageDto = {
  items: [{ id: 'n-1', titleText: 'Title', bodyText: 'Body', at: '2026-11-05T09:30:00Z', read: false, link: null }],
  hasMore: true,
};

describe('NotificationsClient', () => {
  let client: NotificationsClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideTestAppConfig(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BROWSER_STORAGE, useValue: new MemoryStorage() },
      ],
    });
    client = TestBed.inject(NotificationsClient);
    http = TestBed.inject(HttpTestingController);
  });

  it('builds the paths under api/notifications, escaping the id', () => {
    expect(NotificationsApiPaths.List).toBe('api/notifications');
    expect(NotificationsApiPaths.UnreadCount).toBe('api/notifications/unread-count');
    expect(notificationReadPath('a/b c')).toBe('api/notifications/a%2Fb%20c/read');
  });

  it('asks for the newest page in the given language, without a cursor', async () => {
    const answer = firstValueFrom(client.page({ before: null, take: 20, language: AppLanguages.English, context: quietContext() }));
    const request = http.expectOne(candidate => candidate.url === `${TEST_API_BASE_URL}/${NotificationsApiPaths.List}`);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get(NotificationsQueryParams.Take)).toBe('20');
    expect(request.request.params.has(NotificationsQueryParams.Before)).toBe(false);
    expect(request.request.headers.get(ACCEPT_LANGUAGE_HEADER)).toBe(AppLanguages.English);
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush(PAGE);
    expect(await answer).toEqual(PAGE);
    http.verify();
  });

  it('sends no take when the caller names no page size, so the server setting decides', () => {
    client.page({ before: null, language: AppLanguages.Arabic }).subscribe();
    const request = http.expectOne(candidate => candidate.url === `${TEST_API_BASE_URL}/${NotificationsApiPaths.List}`);
    expect(request.request.params.has(NotificationsQueryParams.Take)).toBe(false);
    request.flush(PAGE);
    http.verify();
  });

  it('asks for older notifications with the before cursor', () => {
    client.page({ before: 'n-20', take: 20, language: AppLanguages.Arabic }).subscribe();
    const request = http.expectOne(candidate => candidate.url === `${TEST_API_BASE_URL}/${NotificationsApiPaths.List}`);
    expect(request.request.params.get(NotificationsQueryParams.Before)).toBe('n-20');
    expect(request.request.headers.get(ACCEPT_LANGUAGE_HEADER)).toBe(AppLanguages.Arabic);
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(false);
    request.flush(PAGE);
    http.verify();
  });

  it('reads the unread count and posts mark-read', async () => {
    const count = firstValueFrom(client.unreadCount());
    http.expectOne(`${TEST_API_BASE_URL}/${NotificationsApiPaths.UnreadCount}`).flush({ count: 3 });
    expect(await count).toEqual({ count: 3 });

    client.markRead('n-1').subscribe();
    const request = http.expectOne(`${TEST_API_BASE_URL}/api/notifications/n-1/read`);
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    http.verify();
  });

  it('posts the admin test send', () => {
    client.sendTest().subscribe();
    const request = http.expectOne(`${TEST_API_BASE_URL}/api/admin/notifications/test`);
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 202, statusText: 'Accepted' });
    http.verify();
  });
});
