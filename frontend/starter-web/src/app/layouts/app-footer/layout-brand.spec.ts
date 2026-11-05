import { TestBed } from '@angular/core/testing';
import type { DgaBranding } from '@codelab/ng-dga';
import { describe, expect, it } from 'vitest';
import { provideTestAppConfig, TEST_APP_CONFIG } from '../../core/config/app-config.testing';
import { AppLanguages } from '../../core/i18n/language';
import { fakeAuth, fakeLanguage, layoutTestProviders } from '../layout.testing';
import { LayoutChromeTranslationKeys } from '../portal-shell/layout-chrome-translation-keys';
import { brandLogoFor, brandNameFor, LayoutBrand } from './layout-brand';

const BRANDING: DgaBranding = {
  primaryColor: '#1b8354',
  logoSrc: 'branding/logo.svg',
  appName: { ar: 'بوابة الخدمات', en: 'Services Portal' },
};

describe('brandNameFor', () => {
  it('picks the name in the language', () => {
    expect(brandNameFor(BRANDING, AppLanguages.Arabic)).toBe('بوابة الخدمات');
    expect(brandNameFor(BRANDING, AppLanguages.English)).toBe('Services Portal');
  });

  it('is null without branding', () => {
    expect(brandNameFor(undefined, AppLanguages.English)).toBeNull();
  });
});

describe('brandLogoFor', () => {
  it('returns the configured logo, and null for none or an empty one', () => {
    expect(brandLogoFor(BRANDING)).toBe('branding/logo.svg');
    expect(brandLogoFor({ ...BRANDING, logoSrc: '' })).toBeNull();
    expect(brandLogoFor(undefined)).toBeNull();
  });
});

describe('LayoutBrand', () => {
  function brandWith(branding: DgaBranding | undefined, language = fakeLanguage(AppLanguages.Arabic)): LayoutBrand {
    TestBed.configureTestingModule({
      providers: [
        ...layoutTestProviders(fakeAuth(), language),
        provideTestAppConfig({ ...TEST_APP_CONFIG, branding }),
      ],
    });
    return TestBed.inject(LayoutBrand);
  }

  it('follows the current language', () => {
    const language = fakeLanguage(AppLanguages.Arabic);
    const brand = brandWith(BRANDING, language);
    expect(brand.name()).toBe('بوابة الخدمات');
    language.current.set(AppLanguages.English);
    expect(brand.name()).toBe('Services Portal');
    expect(brand.logoSrc).toBe('branding/logo.svg');
  });

  it('falls back to the translated default name and no logo without branding', () => {
    const brand = brandWith(undefined);
    expect(brand.name()).toBe(LayoutChromeTranslationKeys.BrandName);
    expect(brand.logoSrc).toBeNull();
  });
});
