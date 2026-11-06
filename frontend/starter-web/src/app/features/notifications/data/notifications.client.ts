import { HttpParams, type HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../core/api/api.service';
import { API_PREFIX } from '../../../core/api/clients/api-paths';
import type { AppLanguage } from '../../../core/i18n/language';

const NOTIFICATIONS_PATH = `${API_PREFIX}/notifications`;
const PATH_SEPARATOR = '/';
const READ_SEGMENT = 'read';
const UNREAD_COUNT_SEGMENT = 'unread-count';
const ADMIN_TEST_PATH = `${API_PREFIX}/admin/notifications/test`;

/** The notifications endpoints, relative to the API root (no leading slash). */
export const NotificationsApiPaths = {
  /** `GET`: the reader's notifications, newest first, one page per call (`?before=<id>&take=<n>`). */
  List: NOTIFICATIONS_PATH,
  /** `GET`: `{ count }`, every unread notification of the reader. */
  UnreadCount: `${NOTIFICATIONS_PATH}${PATH_SEPARATOR}${UNREAD_COUNT_SEGMENT}`,
  /** `POST` (202, `notifications.test`): sends the caller the test notification on every enabled channel. */
  SendTest: ADMIN_TEST_PATH,
} as const;

/** `POST api/notifications/{id}/read` (204): marks one notification read. */
export function notificationReadPath(id: string): string {
  return `${NOTIFICATIONS_PATH}${PATH_SEPARATOR}${encodeURIComponent(id)}${PATH_SEPARATOR}${READ_SEGMENT}`;
}

/** The query parameters of the list call. */
export const NotificationsQueryParams = {
  /** The id of the oldest notification already loaded: the page holds the ones older than it. */
  Before: 'before',
  /** The page size. */
  Take: 'take',
} as const;

/** The request header the server reads the reply language from (`ICurrentLanguage`). */
export const ACCEPT_LANGUAGE_HEADER = 'Accept-Language';

/** One notification as the server sends it: texts already in the language the request asked for. */
export interface NotificationDto {
  /** The notification's public id (opaque). */
  readonly id: string;
  readonly titleText: string;
  readonly bodyText: string;
  /** When it was raised, ISO 8601 UTC. */
  readonly at: string;
  readonly read: boolean;
  /** The in-app link it opens, or null. */
  readonly link: string | null;
}

/** One page of the list: newest first, and whether older ones remain. */
export interface NotificationPageDto {
  readonly items: readonly NotificationDto[];
  readonly hasMore: boolean;
}

export interface UnreadCountDto {
  readonly count: number;
}

/** What one list call asks for. */
export interface NotificationPageQuery {
  /** The id of the oldest notification loaded so far; null for the newest page. */
  readonly before: string | null;
  /**
   * How many to return. Omit it to get the server's page size (the `notifications.page-size` runtime setting, which an
   * administrator changes on the settings screen); a value here overrides that setting.
   */
  readonly take?: number;
  /** The language the texts come back in (sent as `Accept-Language`). */
  readonly language: AppLanguage;
  /** Per-request context (a quiet call skips the error dialog). */
  readonly context?: HttpContext;
}

/**
 * The signed-in reader's in-app notifications, a thin wrapper over `ApiService` (bearer and signing as every session
 * call). Errors arrive as the `HttpErrorResponse`; the caller picks the context (quiet for background calls).
 */
@Injectable({ providedIn: 'root' })
export class NotificationsClient {
  private readonly api = inject(ApiService);

  page(query: NotificationPageQuery): Observable<NotificationPageDto> {
    let params = new HttpParams();
    if (query.take !== undefined) {
      params = params.set(NotificationsQueryParams.Take, query.take);
    }
    if (query.before !== null) {
      params = params.set(NotificationsQueryParams.Before, query.before);
    }
    return this.api.get<NotificationPageDto>(NotificationsApiPaths.List, {
      params,
      headers: { [ACCEPT_LANGUAGE_HEADER]: query.language },
      context: query.context,
    });
  }

  unreadCount(context?: HttpContext): Observable<UnreadCountDto> {
    return this.api.get<UnreadCountDto>(NotificationsApiPaths.UnreadCount, { context });
  }

  markRead(id: string): Observable<void> {
    return this.api.post<void>(notificationReadPath(id), null);
  }

  sendTest(): Observable<void> {
    return this.api.post<void>(NotificationsApiPaths.SendTest, null);
  }
}
