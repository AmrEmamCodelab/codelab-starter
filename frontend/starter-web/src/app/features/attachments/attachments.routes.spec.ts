import { TestBed } from '@angular/core/testing';
import type { CanMatchFn, Route } from '@angular/router';
import { describe, expect, it } from 'vitest';
import ar from '../../../assets/i18n/ar.json';
import en from '../../../assets/i18n/en.json';
import { SHELL_ROUTE } from '../../app.routes';
import { flattenTranslations } from '../../core/i18n/translation.service';
import { RouteDataKeys } from '../../core/routing/route-paths';
import { AttachmentsPermissionKeys } from '../../shared/record-attachments/attachments-permission-keys';
import { ATTACHMENTS_DEMO_ENABLED, attachmentsDemoEnabledGuard } from './attachments-demo-enabled.guard';
import { ATTACHMENTS_DEMO_TRANSLATION_KEYS } from './attachments-translation-keys';
import { ATTACHMENTS_DEMO_LINK, ATTACHMENTS_DEMO_ROUTE_PATH, ATTACHMENTS_ROUTES } from './attachments.routes';

function demoRoute(): Route {
  const route = ATTACHMENTS_ROUTES.find((candidate) => candidate.path === ATTACHMENTS_DEMO_ROUTE_PATH);
  if (!route) throw new Error('the demo route is missing');
  return route;
}

function matches(enabled: boolean): unknown {
  TestBed.configureTestingModule({ providers: [{ provide: ATTACHMENTS_DEMO_ENABLED, useValue: enabled }] });
  return TestBed.runInInjectionContext(() => attachmentsDemoEnabledGuard(demoRoute(), [], {} as Parameters<CanMatchFn>[2]));
}

describe('attachments routes', () => {
  it('mounts the demo page inside the staff shell at /attachments/demo', () => {
    expect(SHELL_ROUTE.children).toContain(demoRoute());
    expect(ATTACHMENTS_DEMO_LINK).toBe('/attachments/demo');
  });

  it('gates the demo page by attachments.view with the permission guard', () => {
    const route = demoRoute();
    expect(route.data?.[RouteDataKeys.Permission]).toBe(AttachmentsPermissionKeys.View);
    expect(route.canActivate).toHaveLength(1);
  });

  it('matches only in a development build', () => {
    expect(demoRoute().canMatch).toEqual([attachmentsDemoEnabledGuard]);
    expect(matches(true)).toBe(true);
    TestBed.resetTestingModule();
    expect(matches(false)).toBe(false);
  });

  it('defaults to the build mode (development under the test runner)', () => {
    expect(TestBed.inject(ATTACHMENTS_DEMO_ENABLED)).toBe(true);
  });

  it.each([
    ['ar', flattenTranslations(ar)],
    ['en', flattenTranslations(en)],
  ] as const)('has every demo page key in the %s bundle', (_language, bundle) => {
    expect(ATTACHMENTS_DEMO_TRANSLATION_KEYS.filter((key) => !bundle[key])).toEqual([]);
  });
});
