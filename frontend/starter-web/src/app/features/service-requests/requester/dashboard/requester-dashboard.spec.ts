import { describe, expect, it } from 'vitest';
import { SERVICE_REQUESTS_PERMISSION_KEYS, ServiceRequestStatus } from '../../data';
import { serviceRequestsRequesterLinks } from '../requester-paths';
import { ServiceRequestsRequesterTranslationKeys as Keys } from '../requester-translation-keys';
import { TEST_SERVICE_REQUEST_ID } from '../requester.testing';
import { REQUESTER_DASHBOARD_KPIS, requestLink, requesterKpiTiles, requesterQuickActions } from './requester-dashboard';

const links = serviceRequestsRequesterLinks('/service-requests');

describe('requesterKpiTiles', () => {
  it('has one "all" figure and one per listed status, in order', () => {
    expect(REQUESTER_DASHBOARD_KPIS.filter((kpi) => kpi.status === null)).toHaveLength(1);
    expect(REQUESTER_DASHBOARD_KPIS.map((kpi) => kpi.status)).toEqual([
      null,
      ServiceRequestStatus.Draft,
      ServiceRequestStatus.Submitted,
      ServiceRequestStatus.UnderReview,
      ServiceRequestStatus.Returned,
      ServiceRequestStatus.Approved,
      ServiceRequestStatus.Rejected,
    ]);
  });

  it('shows the known totals, null for the unknown ones, and links each tile to my requests', () => {
    const tiles = requesterKpiTiles(new Map([[Keys.Dashboard.KpiAll, 4], [Keys.Dashboard.KpiDraft, 0]]), links.mine);
    expect(tiles[0]).toEqual({ labelKey: Keys.Dashboard.KpiAll, value: 4, icon: 'file', link: '/service-requests/mine' });
    expect(tiles[1]?.value).toBe(0);
    expect(tiles[2]?.value).toBeNull();
  });
});

describe('requesterQuickActions', () => {
  const holding = (...keys: string[]) => (key: string) => keys.includes(key);

  it('offers each action only to a user who may follow it', () => {
    expect(requesterQuickActions({ links, has: holding(), notificationsLink: null })).toEqual([]);
    expect(
      requesterQuickActions({ links, has: holding(SERVICE_REQUESTS_PERMISSION_KEYS.Create), notificationsLink: null }).map((a) => a.link),
    ).toEqual(['/service-requests/new']);
    expect(
      requesterQuickActions({ links, has: holding(SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn), notificationsLink: null }).map((a) => a.link),
    ).toEqual(['/service-requests/mine']);
  });

  it('links the notifications feed when the shell has one', () => {
    const actions = requesterQuickActions({ links, has: holding(), notificationsLink: '/notifications' });
    expect(actions).toEqual([{ labelKey: Keys.Dashboard.ActionNotifications, link: '/notifications', icon: 'bell' }]);
  });
});

describe('requestLink', () => {
  it('sends a draft back to the wizard and any other request to its detail page', () => {
    expect(requestLink({ id: TEST_SERVICE_REQUEST_ID, status: ServiceRequestStatus.Draft }, links)).toBe(links.draft(TEST_SERVICE_REQUEST_ID));
    expect(requestLink({ id: TEST_SERVICE_REQUEST_ID, status: ServiceRequestStatus.Returned }, links)).toBe(links.detail(TEST_SERVICE_REQUEST_ID));
  });
});
