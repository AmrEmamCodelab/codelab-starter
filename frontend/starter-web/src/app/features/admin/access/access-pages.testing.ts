import { Component, signal, type Provider, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, type Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AppLanguages } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslationService } from '../../../core/i18n/translation.service';
import { PermissionStore } from '../../../core/permissions/permission.store';
import { echoTranslate } from '../../../patterns/patterns.testing';
import { accessTestProviders, type FakeAccessClient, type FakePermissionSync } from './access.testing';

/** A page the specs navigate to after a create (stands in for the real editor). */
@Component({ template: '' })
export class StubPageComponent {}

/** Spec-only: renders an access page inside a real router, with echo translations and the given permissions. */
export async function renderAccessPage<T>(options: {
  readonly routes: Routes;
  readonly url: string;
  readonly component: Type<T>;
  readonly client: FakeAccessClient;
  readonly sync?: FakePermissionSync;
  readonly permissions: readonly string[];
  readonly providers?: Provider[];
}): Promise<{ harness: RouterTestingHarness; page: T; host: HTMLElement }> {
  TestBed.configureTestingModule({
    providers: [
      provideRouter(options.routes),
      ...accessTestProviders(options.client, options.sync),
      { provide: TranslationService, useValue: { translate: echoTranslate, has: () => false } },
      { provide: LanguageService, useValue: { current: signal(AppLanguages.English) } },
      ...(options.providers ?? []),
    ],
  });
  TestBed.inject(PermissionStore).set(options.permissions);
  const harness = await RouterTestingHarness.create();
  const page = await harness.navigateByUrl(options.url, options.component);
  await settle(harness);
  return { harness, page, host: harness.routeNativeElement as HTMLElement };
}

/** Lets pending promises (the stores' loads) finish and re-renders. */
export async function settle(harness: RouterTestingHarness): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  harness.detectChanges();
}

export function texts(root: ParentNode, selector: string): string[] {
  return [...root.querySelectorAll(selector)].map((element) => element.textContent?.trim() ?? '');
}

/** Clicks the native button inside a `<dga-button>` (or the element itself). */
export function click(root: ParentNode, selector: string): void {
  const element = root.querySelector(selector);
  if (!element) {
    throw new Error(`nothing matches ${selector}`);
  }
  const target = element.querySelector('button') ?? element;
  (target as HTMLElement).click();
}
