import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { LanguageService } from '../../core/i18n/language.service';
import { AccessRefusal, RouteDataKeys, RoutePaths } from '../../core/routing/route-paths';
import { opsTestProviders } from '../../core/testing/ops.testing';
import { AccessKeys, AccessPage, accessTextKeys, refusalFrom } from './access.page';

describe('access helpers', () => {
  it('reads the refusal from route data, defaulting to sign-in required', () => {
    expect(refusalFrom({ [RouteDataKeys.Refusal]: AccessRefusal.Forbidden })).toBe(AccessRefusal.Forbidden);
    expect(refusalFrom({ [RouteDataKeys.Refusal]: AccessRefusal.Unauthenticated })).toBe(AccessRefusal.Unauthenticated);
    expect(refusalFrom({})).toBe(AccessRefusal.Unauthenticated);
  });

  it('picks the texts per refusal', () => {
    expect(accessTextKeys(AccessRefusal.Forbidden)).toEqual({ title: AccessKeys.Forbidden.Title, body: AccessKeys.Forbidden.Body });
    expect(accessTextKeys(AccessRefusal.Unauthenticated).title).toBe(AccessKeys.Unauthenticated.Title);
  });
});

describe('AccessPage', () => {
  const toggle = vi.fn(async () => undefined);

  function render(refusal: AccessRefusal) {
    TestBed.configureTestingModule({
      providers: [
        ...opsTestProviders(),
        { provide: ActivatedRoute, useValue: { data: of({ [RouteDataKeys.Refusal]: refusal }) } },
        { provide: LanguageService, useValue: { toggle } },
      ],
    });
    const fixture = TestBed.createComponent(AccessPage);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => toggle.mockClear());

  it('explains a 403 and sends "try again" back to the overview', () => {
    const root = render(AccessRefusal.Forbidden);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    expect(root.textContent).toContain(AccessKeys.Forbidden.Title);
    root.querySelector<HTMLButtonElement>('[data-testid="access-try-again"] button')?.click();
    expect(navigate).toHaveBeenCalledWith([RoutePaths.Overview]);
  });

  it('explains a 401 and offers the language switch', () => {
    const root = render(AccessRefusal.Unauthenticated);
    expect(root.textContent).toContain(AccessKeys.Unauthenticated.Body);
    const buttons = root.querySelectorAll<HTMLButtonElement>('dga-button button');
    buttons[buttons.length - 1]?.click();
    expect(toggle).toHaveBeenCalled();
  });
});
