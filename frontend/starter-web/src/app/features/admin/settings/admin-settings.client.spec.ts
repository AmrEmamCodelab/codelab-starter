import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL, provideTestAppConfig } from '../../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../../core/http/http-context';
import { BROWSER_STORAGE } from '../../../core/session/app-storage';
import { MemoryStorage } from '../../../core/session/memory-storage';
import { AdminSettingsClient, SettingsApiPaths } from './admin-settings.client';
import { runtimeSetting } from './settings.testing';

const url = (path: string) => `${TEST_API_BASE_URL}/${path}`;

describe('AdminSettingsClient', () => {
  let http: HttpTestingController;
  let client: AdminSettingsClient;

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
    client = TestBed.inject(AdminSettingsClient);
  });

  afterEach(() => http.verify());

  it('lists every setting quietly from api/admin/settings', () => {
    let answer: unknown;
    client.list().subscribe((settings) => (answer = settings));
    const request = http.expectOne(url('api/admin/settings'));
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush([runtimeSetting()]);
    expect(answer).toEqual([runtimeSetting()]);
  });

  it('puts the new value under the escaped key', () => {
    client.update('portal/limit x', { value: '30' }).subscribe();
    const request = http.expectOne(url('api/admin/settings/portal%2Flimit%20x'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ value: '30' });
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    expect(SettingsApiPaths.setting('a.b')).toBe('api/admin/settings/a.b');
  });
});
