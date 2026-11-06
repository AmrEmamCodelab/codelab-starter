import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { Subject, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import type { RecordListItem, RecordPage } from '../../../../shared/records';
import { ServiceRequestStatus } from '../../data';
import { ServiceRequestsRequesterTranslationKeys as Keys } from '../requester-translation-keys';
import {
  OTHER_SERVICE_REQUEST_ID,
  TEST_SERVICE_REQUEST_ID,
  aListItem,
  aPage,
  clientProviders,
  failing,
  fakeRecordsClient,
  httpRefusal,
  requesterTestProviders,
} from '../requester.testing';
import { MyRequestsComponent } from './my-requests.component';
import { MyRequestsStore } from './my-requests.store';

async function render(mine: ReturnType<typeof vi.fn>): Promise<{ fixture: ComponentFixture<MyRequestsComponent>; root: HTMLElement; page: MyRequestsComponent }> {
  TestBed.configureTestingModule({
    imports: [MyRequestsComponent],
    providers: [...requesterTestProviders(), ...clientProviders({ records: fakeRecordsClient({ mine }) })],
  });
  const fixture = TestBed.createComponent(MyRequestsComponent);
  await settle(fixture);
  return { fixture, root: fixture.nativeElement as HTMLElement, page: fixture.componentInstance };
}

async function settle(fixture: ComponentFixture<MyRequestsComponent>): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

const ROWS: readonly RecordListItem[] = [
  aListItem(),
  aListItem({ id: OTHER_SERVICE_REQUEST_ID, number: null, status: ServiceRequestStatus.Draft, statusKey: 'draft', submittedAt: null }),
];

describe('MyRequestsComponent', () => {
  it('loads the first page of the caller’s own requests', async () => {
    const mine = vi.fn(() => of(aPage(ROWS)));
    const { root } = await render(mine);
    expect(mine).toHaveBeenCalledWith('service-request', { status: null, page: 1, pageSize: 20 });
    expect(root.querySelectorAll('tbody tr')).toHaveLength(2);
  });

  it('shows the number (or none yet), the requester wording and the right link per row', async () => {
    const { root } = await render(vi.fn(() => of(aPage(ROWS))));
    const text = root.querySelector('tbody')?.textContent ?? '';
    expect(text).toContain('SR-2026-001');
    expect(text).toContain(Keys.Mine.NoNumber);
    expect(text).toContain('serviceRequests.status.submitted.requester');
    expect(text).toContain('serviceRequests.status.draft.requester');
    const links = Array.from(root.querySelectorAll<HTMLAnchorElement>('[data-testid="sr-mine-open"]'));
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      `/service-requests/${TEST_SERVICE_REQUEST_ID}`,
      `/service-requests/drafts/${OTHER_SERVICE_REQUEST_ID}`,
    ]);
    expect(links.map((link) => link.textContent?.trim())).toEqual([Keys.Mine.Open, Keys.Mine.Continue]);
  });

  it('filters by status and starts again at the first page', async () => {
    const mine = vi.fn(() => of(aPage(ROWS, 45)));
    const { fixture, page } = await render(mine);
    page.page.set(2);
    await settle(fixture);
    expect(mine).toHaveBeenLastCalledWith('service-request', { status: null, page: 2, pageSize: 20 });
    page.filterBy(ServiceRequestStatus.Returned);
    await settle(fixture);
    expect(mine).toHaveBeenLastCalledWith('service-request', { status: ServiceRequestStatus.Returned, page: 1, pageSize: 20 });
  });

  it('shows the empty text when the requester has no request', async () => {
    const { root } = await render(vi.fn(() => of(aPage([]))));
    expect(root.textContent).toContain(Keys.Mine.Empty);
  });

  it('shows a failure and retries the same query', async () => {
    const mine = vi.fn().mockReturnValueOnce(failing(httpRefusal(HttpStatus.serverError, 'INTERNAL_ERROR'))).mockReturnValue(of(aPage(ROWS)));
    const { fixture, root } = await render(mine);
    expect(root.textContent).toContain('errors.INTERNAL_ERROR');
    fixture.debugElement.injector.get(MyRequestsStore).reload();
    await settle(fixture);
    expect(mine).toHaveBeenCalledTimes(2);
    expect(root.querySelectorAll('tbody tr')).toHaveLength(2);
  });
});

describe('MyRequestsStore', () => {
  it('drops the answer of a query that a newer one replaced', () => {
    const first = new Subject<RecordPage<RecordListItem>>();
    const mine = vi.fn().mockReturnValueOnce(first).mockReturnValue(of(aPage(ROWS)));
    TestBed.configureTestingModule({ providers: [...requesterTestProviders(), ...clientProviders({ records: fakeRecordsClient({ mine }) }), MyRequestsStore] });
    const store = TestBed.inject(MyRequestsStore);
    store.load({ page: 1, status: null });
    store.load({ page: 2, status: null });
    first.next(aPage([aListItem()], 99));
    expect(store.result().total).toBe(2);
    expect(store.loading()).toBe(false);
  });
});
