import type { ApiError } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { DetailViewState, detailViewState } from './requester-detail-state';

const NOT_FOUND = 404;
const ERROR: ApiError = { status: NOT_FOUND, code: 'NOT_FOUND', fieldErrors: [] };

describe('detailViewState', () => {
  it('shows the record when it is neither loading nor failed', () => {
    expect(detailViewState({ loading: false, error: null })).toBe(DetailViewState.Ready);
  });

  it('shows loading while loading', () => {
    expect(detailViewState({ loading: true, error: null })).toBe(DetailViewState.Loading);
  });

  it('lets an error win over loading, so the reference code stays readable during a retry', () => {
    expect(detailViewState({ loading: false, error: ERROR })).toBe(DetailViewState.Error);
    expect(detailViewState({ loading: true, error: ERROR })).toBe(DetailViewState.Error);
  });
});
