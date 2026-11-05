import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  DgaButtonComponent,
  DgaCardComponent,
  DgaPageFeedbackComponent,
  DgaPortalPageHeaderComponent,
  DgaSelectComponent,
  DgaTextareaComponent,
  DgaTextInputComponent,
  type DgaPageFeedbackState,
  type DgaSelectOption,
} from '@codelab/ng-dga';
import { AppConfigService } from '../../../core/config/app-config.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { PortalLinks, portalBreadcrumbs } from '../portal-links';
import { PortalCommonTranslationKeys } from '../portal-translation-keys';
import {
  CONTACT_TOPICS,
  ContactFields,
  ContactLimits,
  EMPTY_CONTACT_FORM,
  contactFormErrors,
  hasContactErrors,
  type ContactField,
  type ContactFormValue,
  type ContactTopicId,
} from './contact-form';
import { serverFieldMessageKey } from './contact-outcome';
import { ContactTranslationKeys } from './contact-translation-keys';
import { PortalContactStore } from './portal-contact.store';

/** The order the fields appear in, so a failed submit focuses the first one to fix. */
const FIELD_ORDER: readonly ContactField[] = [ContactFields.FullName, ContactFields.Email, ContactFields.TopicId, ContactFields.Message];

/** The message box's starting height, in text rows (it grows with the text). */
const MESSAGE_ROWS = 6;

/** The info card sits beside the form's own heading: both are level 2 under the page `h1`. */
const INFO_HEADING_LEVEL = 2;

const NO_FIELD_TOUCHED:Readonly<Record<ContactField, boolean>> = {
  [ContactFields.FullName]: false,
  [ContactFields.Email]: false,
  [ContactFields.TopicId]: false,
  [ContactFields.Message]: false,
};

/**
 * `/portal/contact` (public): the contact form (name, email, topic, message) beside the ways and hours to reach the
 * team. Each field shows its client-side check once it was left or after the first submit, then the server's field
 * error (problem+json `errors`) until the reader edits it. The outcome (sent with its reference number, refused,
 * rate-limited, failed with a reference code) shows in a `dga-page-feedback` banner that takes focus. The form is
 * offered only when `config.json` turns `portalContactForm` on; otherwise the page shows a "coming soon" notice.
 */
@Component({
  selector: 'app-contact-page',
  imports: [
    DgaButtonComponent,
    DgaCardComponent,
    DgaPageFeedbackComponent,
    DgaPortalPageHeaderComponent,
    DgaSelectComponent,
    DgaTextareaComponent,
    DgaTextInputComponent,
    RouterLink,
    TranslatePipe,
  ],
  providers: [PortalContactStore],
  templateUrl: './contact-page.component.html',
  styleUrl: './contact-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContactPageComponent {
  private readonly translations = inject(TranslationService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly store = inject(PortalContactStore);
  /** The form shows only once `config.json` says its endpoint is deployed; until then a "coming soon" notice. */
  protected readonly formEnabled = inject(AppConfigService).portalContactForm;

  protected readonly keys = ContactTranslationKeys;
  protected readonly common = PortalCommonTranslationKeys;
  protected readonly fields = ContactFields;
  protected readonly limits = ContactLimits;
  protected readonly faqLink = PortalLinks.Faq;
  protected readonly messageRows = MESSAGE_ROWS;
  protected readonly infoHeadingLevel = INFO_HEADING_LEVEL;

  readonly form = signal<ContactFormValue>(EMPTY_CONTACT_FORM);
  readonly touched = signal<Readonly<Record<ContactField, boolean>>>(NO_FIELD_TOUCHED);
  readonly submitted = signal(false);

  private readonly clientErrors = computed(() => contactFormErrors(this.form()));
  readonly topicOptions = computed<DgaSelectOption<ContactTopicId>[]>(() =>
    CONTACT_TOPICS.map(topic => ({ value: topic.id, label: this.translations.translate(topic.labelKey) })),
  );
  readonly breadcrumbs = computed(() =>
    portalBreadcrumbs(key => this.translations.translate(key), ContactTranslationKeys.Title),
  );
  readonly feedback = computed<DgaPageFeedbackState | null>(() => {
    const outcome = this.store.outcome();
    if (outcome === null) {
      return null;
    }
    return {
      kind: outcome.kind,
      title: this.translations.translate(outcome.titleKey),
      message: this.translations.translate(outcome.messageKey, outcome.params),
    };
  });

  /** The translated message under `field`: the client check once shown, else the server's error, else null. */
  errorOf(field: ContactField): string | null {
    const client = this.clientErrors()[field];
    if (client && (this.submitted() || this.touched()[field])) {
      return this.translations.translate(client.key, client.params);
    }
    const serverKey = serverFieldMessageKey(this.store.serverFieldErrors()[field], key => this.translations.has(key));
    return serverKey === null ? null : this.translations.translate(serverKey);
  }

  edit<K extends ContactField>(field: K, value: ContactFormValue[K]): void {
    this.form.update(form => ({ ...form, [field]: value }));
    this.store.clearServerError(field);
  }

  markTouched(field: ContactField, touched: boolean): void {
    this.touched.update(state => ({ ...state, [field]: touched }));
  }

  async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    if (hasContactErrors(this.clientErrors())) {
      this.focusFirstInvalid();
      return;
    }
    // On a server answer the banner takes focus (dga-page-feedback), so the outcome is read first.
    if (await this.store.send(this.form())) {
      this.form.set(EMPTY_CONTACT_FORM);
      this.touched.set(NO_FIELD_TOUCHED);
      this.submitted.set(false);
    }
  }

  private focusFirstInvalid(): void {
    const field = FIELD_ORDER.find(name => this.errorOf(name) !== null);
    if (field === undefined) {
      return;
    }
    this.host.nativeElement.querySelector<HTMLElement>(`[name="${field}"]`)?.focus();
  }
}
