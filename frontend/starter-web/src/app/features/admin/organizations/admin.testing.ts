import { HttpErrorResponse } from '@angular/common/http';
import type { EnvironmentProviders, Provider } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { type PermissionKey } from '../../../core/permissions/permission-keys.generated';
import { PermissionStore } from '../../../core/permissions/permission.store';
import { TranslationService } from '../../../core/i18n/translation.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { echoTranslate, fakeLanguage, type FakeLanguage } from '../../../layouts/layout.testing';
import type { ImportPreviewRow } from '../import/data/import.models';
import { AccountBinding, UserKind, type AdminUser, type UserId } from '../users/data/user.models';
import { OrganizationKind, type Organization, type OrganizationId } from './data/organization.models';

/**
 * Test doubles and fixtures shared by the admin area's specs (users, organizations, import, home). Spec-only:
 * nothing in the app imports this file. Translations echo their key (`key|name=value` with parameters).
 */
export function organization(overrides: Partial<Organization> = {}): Organization {
  return {
    id: 'org-1' as OrganizationId,
    code: 'HQ',
    nameEn: 'Head office',
    nameAr: 'المقر',
    kind: OrganizationKind.InternalOrganization,
    isActive: true,
    ...overrides,
  };
}

export function adminUser(overrides: Partial<AdminUser> = {}): AdminUser {
  return {
    id: 'user-1' as UserId,
    userName: 'sara',
    displayName: 'Sara Ali',
    email: 'sara@example.test',
    loginName: null,
    upn: null,
    kind: UserKind.InternalUser,
    binding: AccountBinding.Password,
    organizationId: null,
    organizationName: null,
    isActive: true,
    hasPassword: true,
    isLockedOut: false,
    isBreakGlass: false,
    ...overrides,
  };
}

export function previewRow(overrides: Partial<ImportPreviewRow> = {}): ImportPreviewRow {
  return {
    rowNumber: 2,
    userName: 'omar',
    displayName: 'Omar Saleh',
    email: 'omar@example.test',
    kind: UserKind.InternalUser,
    binding: AccountBinding.Password,
    organizationCode: null,
    valid: true,
    errors: [],
    ...overrides,
  };
}

/** A problem+json failure as the Angular HTTP client raises it. */
export function problem(status: number, code: string, errors?: Readonly<Record<string, readonly string[]>>): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    error: { code, correlationId: 'trace-1', ...(errors ? { errors } : {}) },
  });
}

/** Providers for a component spec: echo translations, a fake language and an empty router. */
export function adminTestProviders(language: FakeLanguage = fakeLanguage()): (Provider | EnvironmentProviders)[] {
  return [
    provideRouter([]),
    { provide: TranslationService, useValue: { translate: echoTranslate } },
    { provide: LanguageService, useValue: language },
  ];
}

/** Grants exactly these keys to the signed-in user. */
export function grant(...keys: PermissionKey[]): void {
  TestBed.inject(PermissionStore).set(keys);
}

/** The element with `data-testid`, or null. */
export function byTestId(fixture: ComponentFixture<unknown>, id: string): HTMLElement | null {
  return (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${id}"]`);
}

/** Every element with `data-testid`. */
export function allByTestId(fixture: ComponentFixture<unknown>, id: string): HTMLElement[] {
  return [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(`[data-testid="${id}"]`)];
}

/** Clicks the native button inside a `dga-button` (or the element itself). */
export function press(element: HTMLElement | null): void {
  const target = element?.querySelector('button') ?? element;
  if (!target) {
    throw new Error('Nothing to press.');
  }
  target.click();
}

/** Lets pending promises and effects settle, then re-renders. */
export async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let round = 0; round < SETTLE_ROUNDS; round += 1) {
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();
  }
}

/** Enough rounds for a click -> async store call -> signal write -> effect -> render chain. */
const SETTLE_ROUNDS = 3;

/** Sets a native `<select>` and fires its change event. */
export function choose(select: HTMLElement | null, value: string): void {
  const element = select as HTMLSelectElement | null;
  if (!element) {
    throw new Error('No select.');
  }
  element.value = value;
  element.dispatchEvent(new Event('change'));
}
