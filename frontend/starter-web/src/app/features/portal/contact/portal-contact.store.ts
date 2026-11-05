import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslationService } from '../../../core/i18n/translation.service';
import { contactFormErrors, hasContactErrors, type ContactField, type ContactFormValue } from './contact-form';
import {
  contactFailureFrom,
  failureOutcome,
  successOutcome,
  type ContactOutcome,
  type ContactServerFieldErrors,
} from './contact-outcome';
import { PortalContactClient } from './portal-contact.client';

/**
 * The contact form's send flow: one message at a time, the page-level outcome (sent, refused, failed) and the
 * server's field errors. Provided per page.
 */
@Injectable()
export class PortalContactStore {
  private readonly client = inject(PortalContactClient);
  private readonly translations = inject(TranslationService);
  private readonly sending = signal(false);
  private readonly lastOutcome = signal<ContactOutcome | null>(null);
  private readonly serverErrors = signal<ContactServerFieldErrors>({});

  readonly busy = this.sending.asReadonly();
  /** The banner to show, or null. */
  readonly outcome = this.lastOutcome.asReadonly();
  /** The server's message keys per refused field (cleared per field as the reader edits it). */
  readonly serverFieldErrors = this.serverErrors.asReadonly();

  /**
   * Sends the message when the form passes the client checks and no other message is pending.
   *
   * @returns true when the server accepted it (the page then clears the form).
   */
  async send(value: ContactFormValue): Promise<boolean> {
    if (this.sending() || value.topicId === null || hasContactErrors(contactFormErrors(value))) {
      return false;
    }
    this.sending.set(true);
    this.lastOutcome.set(null);
    this.serverErrors.set({});
    try {
      const receipt = await firstValueFrom(this.client.send({
        fullName: value.fullName.trim(),
        email: value.email.trim(),
        topicId: value.topicId,
        message: value.message.trim(),
        language: this.translations.language(),
      }));
      this.lastOutcome.set(successOutcome(receipt));
      return true;
    } catch (error: unknown) {
      const failure = contactFailureFrom(error);
      this.serverErrors.set(failure.fieldErrors);
      this.lastOutcome.set(failureOutcome(failure));
      return false;
    } finally {
      this.sending.set(false);
    }
  }

  /** Drops the server's error for a field the reader is editing. */
  clearServerError(field: ContactField): void {
    if (this.serverErrors()[field] === undefined) {
      return;
    }
    const remaining = { ...this.serverErrors() };
    delete remaining[field];
    this.serverErrors.set(remaining);
  }

  /** Hides the banner (the reader dismissed it). */
  clearOutcome(): void {
    this.lastOutcome.set(null);
  }
}
