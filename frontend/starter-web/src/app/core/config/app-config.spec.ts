import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import {
  AppConfigInvalidError,
  DEFAULT_PERMISSION_REFRESH_SECONDS,
  MAX_PERMISSION_REFRESH_SECONDS,
  joinUrl,
  parseAppConfig,
  RUNTIME_CONFIG_PATH,
} from './app-config';
import { AppConfigService } from './app-config.service';
import { provideTestAppConfig, TEST_APP_CONFIG } from './app-config.testing';
import { loadRuntimeConfig } from './runtime-config.loader';

const valid = {
  apiBaseUrl: 'http://localhost:5180',
  origins: { api: ['http://localhost:5180'], attachment: [] },
  security: { protocolPrefix: 'STARTER' },
  defaultLanguage: 'ar',
};

describe('parseAppConfig', () => {
  it('returns a typed copy of a valid config, defaulting the permission refresh interval', () => {
    expect(parseAppConfig(valid)).toEqual({ ...valid, permissionRefreshSeconds: DEFAULT_PERMISSION_REFRESH_SECONDS });
  });

  it.each([0, 5, MAX_PERMISSION_REFRESH_SECONDS])('accepts a permission refresh interval of %d seconds', seconds => {
    expect(parseAppConfig({ ...valid, permissionRefreshSeconds: seconds }).permissionRefreshSeconds).toBe(seconds);
  });

  it.each([
    ['$', null],
    ['$', []],
    ['apiBaseUrl', { ...valid, apiBaseUrl: ' ' }],
    ['origins', { ...valid, origins: undefined }],
    ['origins.api', { ...valid, origins: { api: 'x', attachment: [] } }],
    ['origins.attachment', { ...valid, origins: { api: [], attachment: [1] } }],
    ['security', { ...valid, security: null }],
    ['security.protocolPrefix', { ...valid, security: {} }],
    ['defaultLanguage', { ...valid, defaultLanguage: 'fr' }],
    ['permissionRefreshSeconds', { ...valid, permissionRefreshSeconds: -1 }],
    ['permissionRefreshSeconds', { ...valid, permissionRefreshSeconds: 1.5 }],
    ['permissionRefreshSeconds', { ...valid, permissionRefreshSeconds: '30' }],
    ['permissionRefreshSeconds', { ...valid, permissionRefreshSeconds: MAX_PERMISSION_REFRESH_SECONDS + 1 }],
  ])('names the bad field %s', (field, raw) => {
    expect(() => parseAppConfig(raw)).toThrow(AppConfigInvalidError);
    try {
      parseAppConfig(raw);
    } catch (error) {
      expect((error as AppConfigInvalidError).field).toBe(field);
    }
  });
});

describe('joinUrl', () => {
  it.each([
    ['http://a/', '/x', 'http://a/x'],
    ['http://a', 'x', 'http://a/x'],
    ['http://a//', '//x/y', 'http://a/x/y'],
    ['http://a/', '', 'http://a'],
  ])('joins %s and %s', (base, path, expected) => {
    expect(joinUrl(base, path)).toBe(expected);
  });
});

describe('AppConfigService', () => {
  it('exposes the configuration', () => {
    TestBed.configureTestingModule({ providers: [provideTestAppConfig()] });
    const service = TestBed.inject(AppConfigService);
    expect(service.apiBaseUrl).toBe(TEST_APP_CONFIG.apiBaseUrl);
    expect(service.origins).toBe(TEST_APP_CONFIG.origins);
    expect(service.protocolPrefix).toBe('STARTER');
    expect(service.defaultLanguage).toBe('ar');
    expect(service.permissionRefreshSeconds).toBe(DEFAULT_PERMISSION_REFRESH_SECONDS);
    expect(service.apiUrl('api/x')).toBe(`${TEST_APP_CONFIG.apiBaseUrl}/api/x`);
  });
});

describe('loadRuntimeConfig', () => {
  it('fetches config.json and validates it', async () => {
    const fetchJson = vi.fn().mockResolvedValue(valid);
    await expect(loadRuntimeConfig(fetchJson)).resolves.toEqual({
      ...valid,
      permissionRefreshSeconds: DEFAULT_PERMISSION_REFRESH_SECONDS,
    });
    expect(fetchJson).toHaveBeenCalledWith(RUNTIME_CONFIG_PATH);
  });

  it('refuses a malformed file', async () => {
    await expect(loadRuntimeConfig(vi.fn().mockResolvedValue({}))).rejects.toBeInstanceOf(AppConfigInvalidError);
  });
});
