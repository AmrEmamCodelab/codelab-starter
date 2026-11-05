import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fakeAuth, layoutTestProviders, type FakeAuth } from '../layout.testing';
import { UserProfileMenuComponent } from './user-profile-menu.component';

describe('UserProfileMenuComponent', () => {
  let auth: FakeAuth;
  let fixture: ComponentFixture<UserProfileMenuComponent>;
  let host: HTMLElement;

  beforeEach(() => {
    auth = fakeAuth({ id: 'user-public-id', displayName: 'Sara Admin' });
    TestBed.configureTestingModule({
      imports: [UserProfileMenuComponent],
      providers: layoutTestProviders(auth),
    });
    fixture = TestBed.createComponent(UserProfileMenuComponent);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);
  });

  afterEach(() => host.remove());

  const trigger = () => host.querySelector('.menu__trigger') as HTMLButtonElement;
  const panel = () => host.querySelector('[role="menu"]');

  function openMenu(): void {
    trigger().click();
    fixture.detectChanges();
  }

  it("shows the signed-in user's name, closed", () => {
    expect(trigger().textContent).toContain('Sara Admin');
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(panel()).toBeNull();
  });

  it('opens and closes on the trigger', () => {
    openMenu();
    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(panel()?.id).toBe(trigger().getAttribute('aria-controls'));
    openMenu();
    expect(panel()).toBeNull();
  });

  it('logs out through AuthService and closes', async () => {
    openMenu();
    (host.querySelector('[role="menuitem"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(auth.logout).toHaveBeenCalledTimes(1);
    expect(panel()).toBeNull();
  });

  it('closes on Escape', () => {
    openMenu();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(panel()).toBeNull();
  });

  it('closes on a click outside, not on a click inside', () => {
    openMenu();
    (panel() as HTMLElement).click();
    fixture.detectChanges();
    expect(panel()).not.toBeNull();

    document.body.click();
    fixture.detectChanges();
    expect(panel()).toBeNull();
  });
});
