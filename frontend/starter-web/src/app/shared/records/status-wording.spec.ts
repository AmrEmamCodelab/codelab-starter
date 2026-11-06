import { describe, expect, it } from 'vitest';
import { WorkflowAudience } from './records.models';
import { StatusWordingPipe } from './status-wording.pipe';
import { statusWordingKey, statusWordingResolver } from './status-wording';

const WORDING = { requesterKey: 'w.requester', staffKey: 'w.staff', adminKey: 'w.admin' };

describe('status wording', () => {
  it.each([
    [WorkflowAudience.Requester, 'w.requester'],
    [WorkflowAudience.Staff, 'w.staff'],
    [WorkflowAudience.Admin, 'w.admin'],
  ])('picks the wording of audience %s', (audience, key) => {
    expect(statusWordingKey(WORDING, audience)).toBe(key);
  });

  it('falls back to the requester wording for an unknown audience', () => {
    expect(statusWordingKey(WORDING, 99 as WorkflowAudience)).toBe('w.requester');
  });

  it('builds catalog-convention keys from a prefix', () => {
    const resolve = statusWordingResolver('testRecords.status');
    expect(resolve('under-review', WorkflowAudience.Staff)).toBe('testRecords.status.under-review.staff');
    expect(resolve('approved', WorkflowAudience.Admin)).toBe('testRecords.status.approved.admin');
    expect(resolve('approved', WorkflowAudience.Requester)).toBe('testRecords.status.approved.requester');
    expect(resolve('approved', 99 as WorkflowAudience)).toBe('testRecords.status.approved.requester');
  });

  it('pipes a wording to the audience key, and nothing to an empty string', () => {
    const pipe = new StatusWordingPipe();
    expect(pipe.transform(WORDING, WorkflowAudience.Staff)).toBe('w.staff');
    expect(pipe.transform(null, WorkflowAudience.Staff)).toBe('');
    expect(pipe.transform(undefined, WorkflowAudience.Admin)).toBe('');
  });
});
