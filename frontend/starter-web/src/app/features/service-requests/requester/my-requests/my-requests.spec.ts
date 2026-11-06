import { describe, expect, it } from 'vitest';
import { ServiceRequestStatus } from '../../data';
import { aListItem, translated } from '../requester.testing';
import { MY_REQUESTS_COLUMNS, MY_REQUESTS_FILTER_STATUSES, myRequestRowId, statusFilterOptions } from './my-requests';

describe('my requests helpers', () => {
  it('lists number, status, created and submitted columns', () => {
    expect(MY_REQUESTS_COLUMNS.map((column) => column.key)).toEqual(['number', 'status', 'createdAt', 'submittedAt']);
  });

  it('offers every status in workflow order, labelled in the requester wording', () => {
    const options = statusFilterOptions(translated);
    expect(options.map((option) => option.value)).toEqual(MY_REQUESTS_FILTER_STATUSES);
    expect(options).toHaveLength(8);
    expect(options.find((option) => option.value === ServiceRequestStatus.UnderReview)?.label).toBe(
      'serviceRequests.status.under-review.requester',
    );
  });

  it('tracks rows by their PublicId', () => {
    const row = aListItem();
    expect(myRequestRowId(row)).toBe(row.id);
  });
});
