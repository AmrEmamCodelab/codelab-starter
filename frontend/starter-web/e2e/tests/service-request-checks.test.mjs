// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { ALL_FLOWS, FlowNames } from '../lib/flow-constants.mjs';
import {
  actorSummary,
  flowVariants,
  recordNumberOf,
  recordNumbersIn,
  requesterCredentials,
  requestTitle,
  timelineCovers,
} from '../lib/service-request-checks.mjs';
import { APPROVED_PATH_ACTIONS, RequesterGrantKeys, SrActions, SrData, SrViewports } from '../lib/service-request-constants.mjs';

const STAMP = '2026-11-07T05-45-00-123Z';
const SECRET = 'not-a-real-password';

describe('requester credentials', () => {
  it('reads the first Development user of the secrets file', () => {
    const text = JSON.stringify({ Identity: { DevUsers: [{ UserName: 'dev.viewer', Password: SECRET }, { UserName: 'other', Password: 'x' }] } });
    expect(requesterCredentials(text)).toEqual({ user: 'dev.viewer', password: SECRET });
  });

  it('refuses a file without a Development user and never echoes a password', () => {
    const missing = requesterCredentials(JSON.stringify({ Identity: { DevUsers: [{ UserName: 'dev.viewer' }] } }));
    expect(missing.error).toContain('Identity:DevUsers:0');
    expect(requesterCredentials(JSON.stringify({ Identity: {} })).error).toContain('New-LocalSecrets');
    const broken = requesterCredentials(`{"Password":"${SECRET}"`);
    expect(broken.error).toContain('not JSON');
    expect(broken.error).not.toContain(SECRET);
  });
});

describe('run variants', () => {
  it('runs every language at the desktop width, then at the phone width', () => {
    expect(flowVariants().map((variant) => variant.label)).toEqual(['ar-1440', 'en-1440', 'ar-390', 'en-390']);
    expect(flowVariants(['en'])).toEqual([
      { language: 'en', viewport: SrViewports.Desktop, label: 'en-1440' },
      { language: 'en', viewport: SrViewports.Mobile, label: 'en-390' },
    ]);
  });
});

describe('record numbers', () => {
  it('finds SR-YYYY-NNN of the year with at least three digits on word edges', () => {
    expect(recordNumbersIn('Request SR-2026-001 and SR-2026-1234, not XSR-2026-002, SR-2026-03 or SR-2025-004', 2026)).toEqual([
      'SR-2026-001',
      'SR-2026-1234',
    ]);
    expect(recordNumbersIn(null, 2026)).toEqual([]);
  });

  it('accepts a text that is exactly one number of the year', () => {
    expect(recordNumberOf('  SR-2026-001\n', 2026)).toBe('SR-2026-001');
    expect(recordNumberOf('SR-2025-001', 2026)).toBeNull();
    expect(recordNumberOf('Number SR-2026-001', 2026)).toBeNull();
    expect(recordNumberOf('', 2026)).toBeNull();
  });
});

describe('request titles', () => {
  it('are unique per run, variant and request and meet the title rule (5-200 characters)', () => {
    const first = requestTitle(STAMP, 'ar-1440', 1);
    const titles = new Set([first, requestTitle(STAMP, 'ar-1440', 2), requestTitle(STAMP, 'en-390', 1), requestTitle('2026-11-08T00-00-00-000Z', 'ar-1440', 1)]);
    expect(titles.size).toBe(4);
    expect(first.startsWith(SrData.TitlePrefix)).toBe(true);
    for (const title of titles) {
      expect(title.length).toBeGreaterThanOrEqual(5);
      expect(title.length).toBeLessThanOrEqual(200);
    }
  });

  it('typed data passes the wizard rules', () => {
    expect(SrData.Description.length).toBeGreaterThanOrEqual(20);
    expect(SrData.Phone).toMatch(/^05[0-9]{8}$/);
    expect(SrData.ReturnReason.trim().length).toBeGreaterThan(0);
    expect(SrData.RejectReason.trim().length).toBeGreaterThan(0);
  });
});

describe('timeline coverage', () => {
  it('accepts the approved path in rendered order, oldest or newest first, with extra entries between', () => {
    const oldestFirst = ['create', 'submit', 'start-review', 'return', 'resubmit', 'start-review', 'approve'];
    expect(timelineCovers(oldestFirst, APPROVED_PATH_ACTIONS)).toEqual({ ok: true, missing: [] });
    expect(timelineCovers([...oldestFirst].reverse(), APPROVED_PATH_ACTIONS)).toEqual({ ok: true, missing: [] });
  });

  it('names the missing steps when the path is incomplete', () => {
    const result = timelineCovers(['submit', 'start-review', 'approve'], APPROVED_PATH_ACTIONS);
    expect(result.ok).toBe(false);
    expect(result.missing).toEqual(['return', 'resubmit', 'start-review', 'approve']);
  });

  it('expects the return, resubmit and second review before the approval', () => {
    expect(APPROVED_PATH_ACTIONS).toEqual(['submit', SrActions.StartReview, SrActions.Return, SrActions.Resubmit, SrActions.StartReview, SrActions.Approve]);
  });
});

describe('timeline actors', () => {
  it('needs every entry to name an actor and at least the requester and the reviewer', () => {
    expect(actorSummary(['By dev.viewer', 'By Administrator', 'By dev.viewer'], 2)).toEqual({ ok: true, distinct: ['By dev.viewer', 'By Administrator'], blank: 0 });
    expect(actorSummary(['By dev.viewer', ' '], 2)).toMatchObject({ ok: false, blank: 1 });
    expect(actorSummary(['By dev.viewer'], 2).ok).toBe(false);
    expect(actorSummary([], 1).ok).toBe(false);
  });
});

describe('flow registration', () => {
  it('lists the service-requests flow and grants only requester keys', () => {
    expect(ALL_FLOWS).toContain(FlowNames.ServiceRequests);
    expect(FlowNames.ServiceRequests).toBe('service-requests');
    expect(RequesterGrantKeys.some((key) => key.endsWith('.decide') || key.endsWith('.review') || key.endsWith('.queue'))).toBe(false);
  });
});
