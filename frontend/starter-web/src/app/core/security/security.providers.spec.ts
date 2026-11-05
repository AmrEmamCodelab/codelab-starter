import { TestBed } from '@angular/core/testing';
import { InvalidProtocolPrefixError, SECURITY_PROTOCOL, SIGNED_SERVICE_URL_CLASSIFIER } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { TEST_APP_CONFIG } from '../config/app-config.testing';
import { provideStarterSecurity } from './security.providers';

describe('provideStarterSecurity', () => {
  it('derives the protocol from the configured prefix', () => {
    TestBed.configureTestingModule({ providers: provideStarterSecurity(TEST_APP_CONFIG) });
    expect(JSON.stringify(TestBed.inject(SECURITY_PROTOCOL))).toContain('STARTER');
  });

  it('signs only calls under the configured API roots', () => {
    TestBed.configureTestingModule({ providers: provideStarterSecurity(TEST_APP_CONFIG) });
    const classify = TestBed.inject(SIGNED_SERVICE_URL_CLASSIFIER);
    expect(classify(`${TEST_APP_CONFIG.apiBaseUrl}/api/me/permissions`)).toBe('/api/me/permissions');
    expect(classify('http://elsewhere.test/api/x')).toBeNull();
  });

  it('refuses a malformed prefix at bootstrap', () => {
    const config = { ...TEST_APP_CONFIG, security: { protocolPrefix: 'bad prefix' } };
    expect(() => provideStarterSecurity(config)).toThrow(InvalidProtocolPrefixError);
  });
});
