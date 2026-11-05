import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { fakeAuth, layoutTestProviders } from '../../../layouts/layout.testing';
import { DashboardPageComponent } from './dashboard-page.component';

describe('DashboardPageComponent', () => {
  it('greets the signed-in user by name', () => {
    TestBed.configureTestingModule({
      imports: [DashboardPageComponent],
      providers: layoutTestProviders(fakeAuth({ id: 'user-public-id', displayName: 'Sara Admin' })),
    });
    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('h1')?.textContent).toBe('dashboard.title');
    expect(host.textContent).toContain('dashboard.welcome|name=Sara Admin');
  });
});
