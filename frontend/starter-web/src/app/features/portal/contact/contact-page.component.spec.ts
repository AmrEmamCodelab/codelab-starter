import { HttpErrorResponse } from '@angular/common/http';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { NEVER, of, throwError, type Observable } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../../core/api/api.service';
import { TEST_APP_CONFIG, provideTestAppConfig } from '../../../core/config/app-config.testing';
import { PortalLinks } from '../portal-links';
import { FakePortalTranslations, TEST_DIRECTIONS, portalTestProviders } from '../portal.testing';
import { CONTACT_TOPICS, ContactFields, ContactLimits, ContactTopicIds } from './contact-form';
import { ContactPageComponent } from './contact-page.component';
import { ContactTranslationKeys as Keys } from './contact-translation-keys';
import { PortalApiPaths } from './portal-contact.client';

type Post = (path: string, body: unknown) => Observable<unknown>;

describe('ContactPageComponent', () => {
  let post: ReturnType<typeof vi.fn<Post>>;
  let fixture: ComponentFixture<ContactPageComponent>;
  let root: HTMLElement;

  beforeEach(() => {
    post = vi.fn<Post>();
    TestBed.configureTestingModule({
      imports: [ContactPageComponent],
      providers: [
        ...portalTestProviders(new FakePortalTranslations()),
        provideTestAppConfig({ ...TEST_APP_CONFIG, portalContactForm: true }),
        { provide: ApiService, useValue: { post } },
      ],
    });
    fixture = TestBed.createComponent(ContactPageComponent);
    fixture.autoDetectChanges();
    root = fixture.nativeElement;
    document.body.appendChild(root);
  });

  afterEach(() => {
    root.remove();
    document.documentElement.removeAttribute('dir');
  });

  function control<T extends HTMLElement>(field: string): T {
    const element = root.querySelector<T>(`[name="${field}"]`);
    if (!element) {
      throw new Error(`no control named ${field}`);
    }
    return element;
  }

  function type(field: string, text: string): void {
    const element = control<HTMLInputElement | HTMLTextAreaElement>(field);
    element.value = text;
    element.dispatchEvent(new Event('input'));
  }

  function chooseTopic(index: number): void {
    const select = control<HTMLSelectElement>(ContactFields.TopicId);
    select.value = String(index);
    select.dispatchEvent(new Event('change'));
  }

  /** Types like a reader: the page renders between the fields. */
  async function fillValidForm(): Promise<void> {
    type(ContactFields.FullName, ' Sara Ali ');
    type(ContactFields.Email, 'sara@example.test');
    chooseTopic(1);
    type(ContactFields.Message, 'Please tell me how to follow my request.');
    await settle();
  }

  async function submit(): Promise<void> {
    root.querySelector<HTMLFormElement>('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  /** Lets the async submit finish (the store awaits the client), then renders. */
  async function settle(): Promise<void> {
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
  }

  function errorTexts(): string[] {
    return [...root.querySelectorAll('.dga-field__message--error')].map(error => error.textContent?.trim() ?? '');
  }

  it.each(TEST_DIRECTIONS)('renders one h1, the four fields and the translated topics (%s)', async (direction) => {
    document.documentElement.setAttribute('dir', direction);
    await fixture.whenStable();

    expect(root.querySelectorAll('h1')).toHaveLength(1);
    expect(root.querySelector('h1')?.textContent?.trim()).toBe(Keys.Title);
    for (const field of Object.values(ContactFields)) {
      expect(control(field)).toBeTruthy();
    }
    const options = [...control<HTMLSelectElement>(ContactFields.TopicId).options].map(option => option.textContent?.trim());
    expect(options).toEqual([Keys.TopicPlaceholder, ...CONTACT_TOPICS.map(topic => topic.labelKey)]);
    expect(root.querySelector('[data-testid="contact-submit"]')?.hasAttribute('data-public-action')).toBe(true);
    expect(root.querySelector('.contact__faq-link')?.getAttribute('href')).toBe(PortalLinks.Faq);
  });

  it('shows the client checks after the first submit, focuses the first field and sends nothing', async () => {
    expect(errorTexts()).toEqual([]);
    await submit();

    expect(post).not.toHaveBeenCalled();
    expect(errorTexts()).toEqual([Keys.FullNameRequired, Keys.EmailRequired, Keys.TopicRequired, Keys.MessageRequired]);
    expect(document.activeElement).toBe(control(ContactFields.FullName));
  });

  it('shows a field check once the reader leaves the field', async () => {
    type(ContactFields.Email, 'not-an-address');
    control(ContactFields.Email).dispatchEvent(new Event('blur'));
    await settle();
    expect(errorTexts()).toEqual([Keys.EmailInvalid]);
  });

  it('sends a valid message, shows the reference number and clears the form', async () => {
    post.mockReturnValue(of({ referenceNumber: 'CM-12' }));
    await fillValidForm();
    await submit();

    expect(post).toHaveBeenCalledWith(
      PortalApiPaths.Contact,
      expect.objectContaining({ fullName: 'Sara Ali', topicId: ContactTopicIds.Request }),
      expect.anything(),
    );
    const banner = root.querySelector('[data-testid="contact-feedback"]');
    expect(banner?.textContent).toContain(Keys.SuccessTitle);
    expect(banner?.textContent).toContain(`${Keys.SuccessBodyWithReference}|reference=CM-12`);
    expect(control<HTMLInputElement>(ContactFields.FullName).value).toBe('');
    expect(control<HTMLTextAreaElement>(ContactFields.Message).value).toBe('');
    expect(errorTexts()).toEqual([]);
  });

  it('shows the server field errors of a 400 under their fields until the reader edits them', async () => {
    post.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400,
      error: { code: 'VALIDATION_FAILED', errors: { email: ['PORTAL_CONTACT_EMAIL_BLOCKED'] } },
    })));
    await fillValidForm();
    await submit();

    expect(errorTexts()).toEqual([Keys.FieldInvalid]);
    expect(root.querySelector('[data-testid="contact-feedback"]')?.textContent).toContain(Keys.ValidationFailed);

    type(ContactFields.Email, 'sara.ali@example.test');
    await settle();
    expect(errorTexts()).toEqual([]);
  });

  it('keeps the form and shows the rate-limit message on 429', async () => {
    post.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429 })));
    await fillValidForm();
    await submit();

    expect(root.querySelector('[data-testid="contact-feedback"]')?.textContent).toContain(Keys.RateLimited);
    expect(control<HTMLInputElement>(ContactFields.FullName).value).toBe(' Sara Ali ');
  });

  it('quotes the reference code of a 500 and hides the banner on dismiss', async () => {
    post.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 500,
      error: { code: 'INTERNAL_ERROR', correlationId: 'trace', referenceCode: 'REF-77' },
    })));
    await fillValidForm();
    await submit();

    const banner = root.querySelector('[data-testid="contact-feedback"]');
    expect(banner?.textContent).toContain(`${Keys.FailedWithReference}|reference=REF-77`);
    banner?.querySelector<HTMLElement>('button')?.click();
    await fixture.whenStable();
    expect(fixture.componentInstance.feedback()).toBeNull();
  });

  it('marks the form busy while sending', async () => {
    post.mockReturnValue(NEVER);
    await fillValidForm();
    await submit();
    expect(root.querySelector('form')?.getAttribute('aria-busy')).toBe('true');
    expect(fixture.componentInstance.store.busy()).toBe(true);
  });

  it('caps the inputs at the server limits', () => {
    expect(control(ContactFields.FullName).getAttribute('maxlength')).toBe(String(ContactLimits.FullNameMaxLength));
    expect(control(ContactFields.Message).getAttribute('maxlength')).toBe(String(ContactLimits.MessageMaxLength));
  });
});

describe('ContactPageComponent without the contact endpoint (portalContactForm off)', () => {
  it('shows a coming-soon notice and the ways to reach the team instead of a form that can only fail', () => {
    const post = vi.fn<Post>();
    TestBed.configureTestingModule({
      imports: [ContactPageComponent],
      providers: [...portalTestProviders(new FakePortalTranslations()), provideTestAppConfig(), { provide: ApiService, useValue: { post } }],
    });
    const fixture = TestBed.createComponent(ContactPageComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('form')).toBeNull();
    expect(root.querySelector('[data-testid="contact-submit"]')).toBeNull();
    expect(root.querySelector('[data-testid="contact-unavailable"]')?.textContent?.trim()).toBe(Keys.FormUnavailable);
    expect(root.querySelector('.contact__aside')).not.toBeNull();
    expect(post).not.toHaveBeenCalled();
  });
});
