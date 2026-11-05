import { createEnvironmentInjector, DOCUMENT, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import {
  applyDgaBranding,
  type DgaBranding,
  DgaBrandingField,
  DgaBrandingInvalidError,
  DgaBrandProperty,
  dgaBrandProperties,
  isDgaHexColor,
  isDgaLogoSrc,
  parseDgaBranding,
} from './dga-branding';
import { DGA_BRANDING, provideDgaBranding } from './dga-branding.providers';

const BRANDING: DgaBranding = {
  primaryColor: '#1B8354',
  logoSrc: 'branding/logo.svg',
  appName: { ar: 'بوابة الخدمات', en: 'Services Portal' },
};

function fieldOf(action: () => unknown): string | null {
  try {
    action();
    return null;
  } catch (error) {
    expect(error).toBeInstanceOf(DgaBrandingInvalidError);
    return (error as DgaBrandingInvalidError).field;
  }
}

function clearBrandProperties(): void {
  for (const property of Object.values(DgaBrandProperty)) document.documentElement.style.removeProperty(property);
}

describe('isDgaHexColor', () => {
  it.each(['#abc', '#ABC', '#1b8354', '#1B8354'])('accepts %s', (value) => expect(isDgaHexColor(value)).toBe(true));

  it.each(['1b8354', '#1b835', '#1b83544f', '#ggg', 'green', 'rgb(0,0,0)', '', null, 12])('refuses %s', (value) => {
    expect(isDgaHexColor(value)).toBe(false);
  });
});

describe('isDgaLogoSrc', () => {
  it.each(['', 'branding/logo.svg', '/branding/logo.png', 'assets/brand/logo-2.webp'])('accepts %s', (value) => {
    expect(isDgaLogoSrc(value)).toBe(true);
  });

  it.each([
    'https://cdn.example.test/logo.svg',
    '//cdn.example.test/logo.svg',
    '/\\evil.example/logo.png',
    '\\/evil.example/logo.png',
    '\\\\evil.example/logo.png',
    '/%5cevil.example/logo.png',
    'branding\\logo.png',
    'javascript:alert(1)',
    'data:image/svg+xml,<svg/>',
    'logo with space.svg',
    'logo".svg',
    'a'.repeat(513),
    null,
  ])('refuses %s', (value) => expect(isDgaLogoSrc(value)).toBe(false));
});

describe('parseDgaBranding', () => {
  it('returns a typed copy with the colour in lower case', () => {
    expect(parseDgaBranding(BRANDING)).toEqual({ ...BRANDING, primaryColor: '#1b8354' });
  });

  it.each([
    [DgaBrandingField.Root, null],
    [DgaBrandingField.Root, []],
    [DgaBrandingField.AppName, { ...BRANDING, appName: 'x' }],
    [DgaBrandingField.PrimaryColor, { ...BRANDING, primaryColor: 'green' }],
    [DgaBrandingField.LogoSrc, { ...BRANDING, logoSrc: 'https://x.test/a.svg' }],
    [DgaBrandingField.LogoSrc, { ...BRANDING, logoSrc: '/\\evil.example/logo.png' }],
    [DgaBrandingField.LogoSrc, { ...BRANDING, logoSrc: '\\/evil.example/logo.png' }],
    [DgaBrandingField.AppNameArabic, { ...BRANDING, appName: { en: 'x' } }],
    [DgaBrandingField.AppNameEnglish, { ...BRANDING, appName: { ar: 'x', en: '  ' } }],
    [DgaBrandingField.AppNameEnglish, { ...BRANDING, appName: { ar: 'x', en: 'n'.repeat(121) } }],
  ])('names the bad field %s', (field, raw) => {
    expect(fieldOf(() => parseDgaBranding(raw))).toBe(field);
  });
});

describe('dgaBrandProperties', () => {
  it('derives hover, active and tint shades from the primary colour with color-mix', () => {
    const properties = dgaBrandProperties('#ABCDEF');
    expect(properties[DgaBrandProperty.Primary]).toBe('#abcdef');
    expect(properties[DgaBrandProperty.PrimaryHover]).toBe('color-mix(in srgb, #abcdef 85%, black)');
    expect(properties[DgaBrandProperty.PrimaryActive]).toBe('color-mix(in srgb, #abcdef 70%, black)');
    expect(properties[DgaBrandProperty.PrimarySubtle]).toBe('color-mix(in srgb, #abcdef 8%, white)');
    expect(properties[DgaBrandProperty.PrimaryMuted]).toBe('color-mix(in srgb, #abcdef 18%, white)');
  });

  it('refuses a colour that is not hex', () => {
    expect(fieldOf(() => dgaBrandProperties('red; background: url(x)'))).toBe(DgaBrandingField.PrimaryColor);
  });
});

describe('applyDgaBranding', () => {
  afterEach(clearBrandProperties);

  it('sets every --dga-brand-* property on the root element', () => {
    applyDgaBranding(document, BRANDING);
    const style = document.documentElement.style;
    expect(style.getPropertyValue(DgaBrandProperty.Primary)).toBe('#1b8354');
    for (const property of Object.values(DgaBrandProperty)) expect(style.getPropertyValue(property)).not.toBe('');
  });

  it('changes nothing when the branding is malformed', () => {
    expect(fieldOf(() => applyDgaBranding(document, { ...BRANDING, primaryColor: '#12' }))).toBe(DgaBrandingField.PrimaryColor);
    expect(document.documentElement.style.getPropertyValue(DgaBrandProperty.Primary)).toBe('');
  });
});

describe('provideDgaBranding', () => {
  afterEach(clearBrandProperties);

  it('provides the validated branding and applies it when the injector starts', () => {
    const parent = TestBed.inject(EnvironmentInjector);
    const injector = createEnvironmentInjector([provideDgaBranding(BRANDING)], parent);
    expect(injector.get(DGA_BRANDING).primaryColor).toBe('#1b8354');
    expect(injector.get(DOCUMENT).documentElement.style.getPropertyValue(DgaBrandProperty.Primary)).toBe('#1b8354');
    injector.destroy();
  });

  it('refuses a malformed branding before anything is provided', () => {
    expect(fieldOf(() => provideDgaBranding({ ...BRANDING, logoSrc: '//evil.test/x.png' }))).toBe(DgaBrandingField.LogoSrc);
  });
});
