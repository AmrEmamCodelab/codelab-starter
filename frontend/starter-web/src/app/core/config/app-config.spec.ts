import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { AppConfigInvalidError, joinUrl, parseAppConfig, RUNTIME_CONFIG_PATH } from './app-config';
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
  it('returns a typed copy of a valid config', () => {
    expect(parseAppConfig(valid)).toEqual(valid);
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
    expect(service.apiUrl('api/x')).toBe(`${TEST_APP_CONFIG.apiBaseUrl}/api/x`);
  });
});

describe('loadRuntimeConfig', () => {
  it('fetches config.json and validates it', async () => {
    const fetchJson = vi.fn().mockResolvedValue(valid);
    await expect(loadRuntimeConfig(fetchJson)).resolves.toEqual(valid);
    expect(fetchJson).toHaveBeenCalledWith(RUNTIME_CONFIG_PATH);
  });

  it('refuses a malformed file', async () => {
    await expect(loadRuntimeConfig(vi.fn().mockResolvedValue({}))).rejects.toBeInstanceOf(AppConfigInvalidError);
  });
});
