import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../core/api/api.service';
import { API_PREFIX } from '../../../core/api/clients/api-paths';
import { selfHandledContext } from '../../../core/http/http-context';
import type { AppLanguage } from '../../../core/i18n/language';
import type { ContactTopicId } from './contact-form';

/** The portal endpoints, relative to the API root (no leading slash). */
export const PortalApiPaths = {
  /** `POST`: one contact message from the public portal (P5 backend). */
  Contact: `${API_PREFIX}/portal/contact-messages`,
} as const;

/** The body of `POST api/portal/contact-messages`: trimmed values, the topic id and the language to reply in. */
export interface ContactMessageBody {
  readonly fullName: string;
  readonly email: string;
  readonly topicId: ContactTopicId;
  readonly message: string;
  readonly language: AppLanguage;
}

/**
 * The server's receipt (201 Created): the reference number the reader can quote later, `CU-<year>-<sequence>` such as
 * `CU-2026-001` (at least three digits). A 204 without a body is still read as sent.
 */
export interface ContactMessageReceipt {
  readonly referenceNumber: string;
}

/**
 * The public portal client, a thin wrapper over `ApiService`. The call is anonymous (no bearer, unsigned: the page
 * works without a session) and self-handled: the contact page shows every failure inline, never the app-wide error
 * dialog, and a 401 never ends a session.
 */
@Injectable({ providedIn: 'root' })
export class PortalContactClient {
  private readonly api = inject(ApiService);

  /**
   * Sends one message. Errors arrive as the `HttpErrorResponse`: 400 problem+json with field `errors`, 429 when the
   * sender is rate-limited, 5xx with a `referenceCode`.
   */
  send(body: ContactMessageBody): Observable<ContactMessageReceipt | null> {
    return this.api.post<ContactMessageReceipt | null>(PortalApiPaths.Contact, body, {
      anonymous: true,
      context: selfHandledContext(),
    });
  }
}
