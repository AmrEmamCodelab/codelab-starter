import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL, provideTestAppConfig } from '../../../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../../../core/http/http-context';
import { BROWSER_STORAGE } from '../../../../core/session/app-storage';
import { MemoryStorage } from '../../../../core/session/memory-storage';
import { contentDocument } from '../portal-content.testing';
import { PortalContentAdminClient } from './portal-content-api';
import { PortalContentPages } from './portal-content.models';

const url = (path: string) => `${TEST_API_BASE_URL}/${path}`;

describe('PortalContentAdminClient', () => {
  let http: HttpTestingController;
  let client: PortalContentAdminClient;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideTestAppConfig(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BROWSER_STORAGE, useValue: new MemoryStorage() },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(PortalContentAdminClient);
  });

  afterEach(() => http.verify());

  it('reads a page quietly', () => {
    let answer: unknown;
    client.get(PortalContentPages.ServiceGuide).subscribe((document) => (answer = document));
    const request = http.expectOne(url('api/admin/portal/content/service-guide'));
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush(contentDocument());
    expect(answer).toEqual(contentDocument());
  });

  it('replaces a page with a PUT of the full section list', () => {
    const body = { sections: [{ id: null, titleAr: 'ع', titleEn: 'T', bodyAr: 'ن', bodyEn: 'B', sortOrder: 1 }] };
    client.replace(PortalContentPages.Faq, body).subscribe();
    const request = http.expectOne(url('api/admin/portal/content/faq'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(body);
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush(contentDocument());
  });
});
