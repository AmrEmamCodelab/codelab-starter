import { TestBed } from '@angular/core/testing';
import { DgaBrandProperty, DgaBrandingInvalidError, type DgaBranding } from '@codelab/ng-dga';
import { afterEach, describe, expect, it } from 'vitest';
import { provideTestAppConfig } from '../../core/config/app-config.testing';
import { SHOWCASE_TEST_CONFIG } from '../showcase.testing';
import { BrandingPreview, SAMPLE_BRAND_COLOURS } from './branding-preview';

const CONFIGURED: DgaBranding = { primaryColor: '#1b8354', logoSrc: '', appName: { ar: 'قالب', en: 'Starter' } };
const SAMPLE = SAMPLE_BRAND_COLOURS[0] ?? '';

function preview(branding?: DgaBranding): BrandingPreview {
  TestBed.configureTestingModule({
    providers: [provideTestAppConfig({ ...SHOWCASE_TEST_CONFIG, branding }), BrandingPreview],
  });
  return TestBed.inject(BrandingPreview);
}

const rootProperty = (name: string) => document.documentElement.style.getPropertyValue(name);

afterEach(() => {
  for (const property of Object.values(DgaBrandProperty)) {
    document.documentElement.style.removeProperty(property);
  }
});

describe('BrandingPreview', () => {
  it('applies a sample colour to the page through the brand properties', () => {
    const subject = preview(CONFIGURED);
    subject.tryColour(SAMPLE);
    expect(rootProperty(DgaBrandProperty.Primary)).toBe(SAMPLE);
    expect(subject.trying()).toBe(SAMPLE);
  });

  it('puts the configured branding back on reset', () => {
    const subject = preview(CONFIGURED);
    subject.tryColour(SAMPLE);
    subject.reset();
    expect(rootProperty(DgaBrandProperty.Primary)).toBe(CONFIGURED.primaryColor);
    expect(subject.trying()).toBeNull();
  });

  it('clears the brand properties on reset when the app has no branding', () => {
    const subject = preview();
    subject.tryColour(SAMPLE);
    subject.reset();
    for (const property of Object.values(DgaBrandProperty)) {
      expect(rootProperty(property), property).toBe('');
    }
  });

  it('leaves the page alone on reset when nothing was previewed', () => {
    const subject = preview(CONFIGURED);
    subject.reset();
    expect(rootProperty(DgaBrandProperty.Primary)).toBe('');
  });

  it('resets when the page that provides it is destroyed', () => {
    const subject = preview();
    subject.tryColour(SAMPLE);
    TestBed.resetTestingModule();
    expect(rootProperty(DgaBrandProperty.Primary)).toBe('');
  });

  it('refuses a colour that is not hex, changing nothing', () => {
    const subject = preview(CONFIGURED);
    expect(() => subject.tryColour('blue')).toThrow(DgaBrandingInvalidError);
    expect(subject.trying()).toBeNull();
    expect(rootProperty(DgaBrandProperty.Primary)).toBe('');
  });

  it('offers valid sample colours', () => {
    for (const colour of SAMPLE_BRAND_COLOURS) {
      expect(colour).toMatch(/^#[0-9a-f]{6}$/);
    }
  });
});
