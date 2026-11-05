import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../../core/api/api.service';
import { SKIP_ERROR_DIALOG, SKIP_SESSION_EXPIRY } from '../../../core/http/http-context';
import { AppLanguages } from '../../../core/i18n/language';
import { ContactTopicIds } from './contact-form';
import { PortalApiPaths, PortalContactClient, type ContactMessageBody } from './portal-contact.client';

const BODY: ContactMessageBody = {
  fullName: 'Sara Ali',
  email: 'sara@example.test',
  topicId: ContactTopicIds.Account,
  message: 'I cannot sign in to my account.',
  language: AppLanguages.Arabic,
};

describe('PortalContactClient', () => {
  it('posts the body anonymously and self-handled to the contact path', async () => {
    const post = vi.fn().mockReturnValue(of({ referenceNumber: 'CM-1' }));
    TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: { post } }] });

    const receipt = await firstValueFrom(TestBed.inject(PortalContactClient).send(BODY));

    expect(receipt).toEqual({ referenceNumber: 'CM-1' });
    expect(post).toHaveBeenCalledTimes(1);
    const [path, body, options] = post.mock.calls[0] as [string, unknown, { anonymous: boolean; context: { get: (token: unknown) => unknown } }];
    expect(path).toBe(PortalApiPaths.Contact);
    expect(path).toBe('api/portal/contact-messages');
    expect(body).toEqual(BODY);
    expect(options.anonymous).toBe(true);
    expect(options.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    expect(options.context.get(SKIP_SESSION_EXPIRY)).toBe(true);
  });
});
