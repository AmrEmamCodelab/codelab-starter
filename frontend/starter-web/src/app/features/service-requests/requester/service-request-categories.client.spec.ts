import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL } from '../../../core/config/app-config.testing';
import { AppLanguages } from '../../../core/i18n/language';
import { SKIP_ERROR_DIALOG } from '../../../core/http/http-context';
import { recordsHttpTestProviders } from '../../../shared/records/records.testing';
import { SERVICE_REQUEST_CATEGORIES_PATH, ServiceRequestCategoriesClient, categoryName } from './service-request-categories.client';
import { TEST_CATEGORY } from './requester.testing';

describe('ServiceRequestCategoriesClient', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('reads the active categories from the public lookup endpoint, quietly', () => {
    TestBed.configureTestingModule({ providers: recordsHttpTestProviders() });
    const answers: unknown[] = [];
    TestBed.inject(ServiceRequestCategoriesClient).list().subscribe((categories) => answers.push(categories));
    const request = TestBed.inject(HttpTestingController).expectOne(`${TEST_API_BASE_URL}/api/lookups/service-request-categories`);
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush([TEST_CATEGORY]);
    expect(answers).toEqual([[TEST_CATEGORY]]);
    expect(SERVICE_REQUEST_CATEGORIES_PATH).toBe('api/lookups/service-request-categories');
  });

  it('names a category in the active language', () => {
    TestBed.configureTestingModule({ providers: recordsHttpTestProviders() });
    expect(categoryName(TEST_CATEGORY, AppLanguages.English)).toBe('Certificates');
    expect(categoryName(TEST_CATEGORY, AppLanguages.Arabic)).toBe('شهادات');
  });
});
