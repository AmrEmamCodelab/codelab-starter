import { createEnvironmentInjector, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DGA_BRANDING } from '@codelab/ng-dga';
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
import { provideAppBranding } from './branding.providers';
import { provideTestAppConfig, TEST_APP_CONFIG } from './app-config.testing';
import { loadRuntimeConfig } from './runtime-config.loader';

const branding = {
  primaryColor: '#0a5c8f',
  logoSrc: 'branding/logo.svg',
  appName: { ar: 'بوابة الخدمات', en: 'Services Portal' },
};

const valid = {
  apiBaseUrl: 'http://localhost:5180',
  origins: { api: ['http://localhost:5180'], attachment: [] },
  security: { protocolPrefix: 'STARTER' },
  defaultLanguage: 'ar',
};

describe('parseAppConfig', () => {
  it('returns a typed copy of a valid config, defaulting the permission refresh interval', () => {
    expect(parseAppConfig(valid)).toEqual({ ...valid, permissionRefreshSeconds: DEFAULT_PERMISSION_REFRESH_SECONDS, showcase: false, portalContactForm: false });
  });

  it('has no branding and no showcase unless the file names them', () => {
    const config = parseAppConfig(valid);
    expect(config.showcase).toBe(false);
    expect('branding' in config).toBe(false);
  });

  it('keeps the public contact form off unless the file turns it on', () => {
    expect(parseAppConfig(valid).portalContactForm).toBe(false);
    expect(parseAppConfig({ ...valid, portalContactForm: true }).portalContactForm).toBe(true);
  });

  it('reads the showcase switch and a valid branding block', () => {
    const config = parseAppConfig({ ...valid, showcase: true, branding: { ...branding, primaryColor: '#0A5C8F' } });
    expect(config.showcase).toBe(true);
    expect(config.branding).toEqual(branding);
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
    ['showcase', { ...valid, showcase: 'true' }],
    ['portalContactForm', { ...valid, portalContactForm: 'true' }],
    ['branding', { ...valid, branding: [] }],
    ['branding.primaryColor', { ...valid, branding: { ...branding, primaryColor: 'green' } }],
    ['branding.logoSrc', { ...valid, branding: { ...branding, logoSrc: 'https://cdn.example.test/logo.svg' } }],
    ['branding.appName', { ...valid, branding: { ...branding, appName: null } }],
    ['branding.appName.ar', { ...valid, branding: { ...branding, appName: { en: 'x' } } }],
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
    expect(service.showcase).toBe(false);
    expect(service.portalContactForm).toBe(false);
    expect(service.branding).toBeUndefined();
    expect(service.apiUrl('api/x')).toBe(`${TEST_APP_CONFIG.apiBaseUrl}/api/x`);
  });
});

describe('AppConfigService with showcase and branding', () => {
  it('exposes them from the configuration', () => {
    TestBed.configureTestingModule({ providers: [provideTestAppConfig({ ...TEST_APP_CONFIG, showcase: true, branding })] });
    const service = TestBed.inject(AppConfigService);
    expect(service.showcase).toBe(true);
    expect(service.branding).toEqual(branding);
  });

  it('exposes the public contact form switch', () => {
    TestBed.configureTestingModule({ providers: [provideTestAppConfig({ ...TEST_APP_CONFIG, portalContactForm: true })] });
    expect(TestBed.inject(AppConfigService).portalContactForm).toBe(true);
  });
});

describe('provideAppBranding', () => {
  it('provides nothing without a branding block', () => {
    expect(provideAppBranding(TEST_APP_CONFIG)).toEqual([]);
  });

  it('provides the branding and applies its colour to the document', () => {
    const injector = createEnvironmentInjector(provideAppBranding({ ...TEST_APP_CONFIG, branding }), TestBed.inject(EnvironmentInjector));
    expect(injector.get(DGA_BRANDING)).toEqual(branding);
    expect(document.documentElement.style.getPropertyValue('--dga-brand-primary')).toBe('#0a5c8f');
    injector.destroy();
    document.documentElement.removeAttribute('style');
  });
});

describe('loadRuntimeConfig', () => {
  it('fetches config.json and validates it (production build: no overrides)', async () => {
    const fetchJson = vi.fn().mockResolvedValue(valid);
    await expect(loadRuntimeConfig(fetchJson, false)).resolves.toEqual({
      ...valid,
      permissionRefreshSeconds: DEFAULT_PERMISSION_REFRESH_SECONDS,
      showcase: false,
      portalContactForm: false,
    });
    expect(fetchJson).toHaveBeenCalledWith(RUNTIME_CONFIG_PATH);
  });

  it('refuses a malformed file', async () => {
    await expect(loadRuntimeConfig(vi.fn().mockResolvedValue({}))).rejects.toBeInstanceOf(AppConfigInvalidError);
  });
});
