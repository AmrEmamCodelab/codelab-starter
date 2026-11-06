import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { REQUESTER_SHELL_OPTIONS } from '../../../../layouts/requester-shell/requester-nav';
import type { MineQuery } from '../../../../shared/records';
import { SERVICE_REQUESTS_PERMISSION_KEYS, ServiceRequestStatus } from '../../data';
import { ServiceRequestsRequesterTranslationKeys as Keys } from '../requester-translation-keys';
import {
  TEST_SERVICE_REQUEST_ID,
  aListItem,
  aPage,
  clientProviders,
  failing,
  fakeRecordsClient,
  httpRefusal,
  requesterTestProviders,
} from '../requester.testing';
import { RequesterDashboardComponent } from './requester-dashboard.component';
import { RECENT_REQUESTS_COUNT } from './requester-dashboard';

/** A mine() fake: the total per status (none: all), and the recent rows on the "all" call. */
function mineAnswering(totals: Partial<Record<ServiceRequestStatus, number>>, all = 3) {
  return vi.fn((_type: string, query: MineQuery = {}) => {
    const status = query.status ?? null;
    if (status === null) return of(aPage([aListItem(), aListItem({ id: TEST_SERVICE_REQUEST_ID, status: ServiceRequestStatus.Draft, statusKey: 'draft', number: null })], all));
    return of(aPage([], totals[status as ServiceRequestStatus] ?? 0));
  });
}

async function render(
  mine: ReturnType<typeof vi.fn>,
  options: { readonly held?: readonly string[]; readonly notificationsLink?: string | null } = {},
): Promise<{ fixture: ComponentFixture<RequesterDashboardComponent>; root: HTMLElement }> {
  TestBed.configureTestingModule({
    imports: [RequesterDashboardComponent],
    providers: [
      ...requesterTestProviders({ held: options.held }),
      ...clientProviders({ records: fakeRecordsClient({ mine }) }),
      { provide: REQUESTER_SHELL_OPTIONS, useValue: { navItems: [], homeLink: '/', notificationsLink: options.notificationsLink ?? null } },
    ],
  });
  const fixture = TestBed.createComponent(RequesterDashboardComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

function tileValue(root: HTMLElement, labelKey: string): string | undefined {
  const tile = Array.from(root.querySelectorAll('.landing__tile')).find((element) => element.textContent?.includes(labelKey));
  return tile?.querySelector('.landing__tile-value')?.textContent?.trim();
}

describe('RequesterDashboardComponent', () => {
  it('counts the requests by status and in all', async () => {
    const mine = mineAnswering({ [ServiceRequestStatus.Returned]: 2, [ServiceRequestStatus.Approved]: 5 }, 9);
    const { root } = await render(mine);
    expect(tileValue(root, Keys.Dashboard.KpiAll)).toBe('9');
    expect(tileValue(root, Keys.Dashboard.KpiReturned)).toBe('2');
    expect(tileValue(root, Keys.Dashboard.KpiApproved)).toBe('5');
    expect(tileValue(root, Keys.Dashboard.KpiDraft)).toBe('0');
    expect(mine).toHaveBeenCalledWith('service-request', { page: 1, pageSize: RECENT_REQUESTS_COUNT });
    expect(mine).toHaveBeenCalledWith('service-request', { status: ServiceRequestStatus.Returned, page: 1, pageSize: 1 });
  });

  it('lists the newest requests in the requester wording, a draft linking back to the wizard', async () => {
    const { root } = await render(mineAnswering({}));
    const links = Array.from(root.querySelectorAll<HTMLAnchorElement>('[data-testid="sr-dashboard-recent-link"]')).map((link) => link.getAttribute('href'));
    expect(links).toEqual([`/service-requests/${TEST_SERVICE_REQUEST_ID}`, `/service-requests/drafts/${TEST_SERVICE_REQUEST_ID}`]);
    expect(root.querySelector('[data-testid="sr-dashboard-recent"]')?.textContent).toContain('serviceRequests.status.submitted.requester');
  });

  it('offers the quick actions the user may follow, and the notifications feed', async () => {
    const { root } = await render(mineAnswering({}), { held: [SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn], notificationsLink: '/notifications' });
    const hrefs = Array.from(root.querySelectorAll<HTMLAnchorElement>('.landing__action')).map((link) => link.getAttribute('href'));
    expect(hrefs).toEqual(['/service-requests/mine', '/notifications']);
  });

  it('says so when there is no request yet', async () => {
    const empty = vi.fn(() => of(aPage([], 0)));
    const { root } = await render(empty);
    expect(root.textContent).toContain(Keys.Dashboard.RecentEmpty);
  });

  it('shows a failure with Retry and loads again', async () => {
    const mine = vi.fn().mockReturnValueOnce(failing(httpRefusal(HttpStatus.serverError, 'INTERNAL_ERROR'))).mockImplementation(mineAnswering({}));
    const { fixture, root } = await render(mine);
    expect(root.textContent).toContain(Keys.Dashboard.LoadFailed);
    expect(tileValue(root, Keys.Dashboard.KpiAll)).not.toBe('3');
    root.querySelector<HTMLButtonElement>('[data-testid="sr-dashboard-retry"] button')?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).not.toContain(Keys.Dashboard.LoadFailed);
    expect(tileValue(root, Keys.Dashboard.KpiAll)).toBe('3');
  });
});
