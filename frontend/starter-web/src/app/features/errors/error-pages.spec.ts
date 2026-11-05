import type { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { fakeAuth, layoutTestProviders, type FakeAuth } from '../../layouts/layout.testing';
import { NotFoundPageComponent } from './not-found/not-found-page.component';
import { UnauthorizedPageComponent } from './unauthorized/unauthorized-page.component';

function setUp(auth: FakeAuth): void {
  TestBed.configureTestingModule({
    imports: [NotFoundPageComponent, UnauthorizedPageComponent],
    providers: [provideRouter([]), ...layoutTestProviders(auth)],
  });
}

function render<T>(component: Type<T>): HTMLElement {
  const fixture = TestBed.createComponent(component);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('NotFoundPageComponent', () => {
  beforeEach(() => setUp(fakeAuth()));

  it('says the page does not exist and links home', () => {
    const host = render(NotFoundPageComponent);
    expect(host.querySelector('[role="heading"]')?.textContent).toContain('error-pages.not-found.title');
    expect(host.querySelector('a')?.getAttribute('href')).toBe('/');
  });
});

describe('UnauthorizedPageComponent', () => {
  it('offers a signed-in user home and signing in as another user', () => {
    const auth = fakeAuth();
    setUp(auth);
    const host = render(UnauthorizedPageComponent);
    expect(host.querySelector('a')?.getAttribute('href')).toBe('/');

    (host.querySelector('dga-button button') as HTMLButtonElement).click();
    expect(auth.logout).toHaveBeenCalledTimes(1);
  });

  it('offers a signed-out visitor the login page', () => {
    setUp(fakeAuth(null));
    const host = render(UnauthorizedPageComponent);
    expect(host.querySelector('a')?.getAttribute('href')).toBe('/login');
    expect(host.querySelector('dga-button')).toBeNull();
  });
});
