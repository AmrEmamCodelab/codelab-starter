import { ApiErrorCodes, referenceCodeFrom } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { SHOWCASE_API_ERROR, SHOWCASE_CORRELATION_ID, ShowcaseState, showcaseListView } from './showcase-state';

const ROWS = ['a', 'b', 'c'] as const;

describe('showcaseListView', () => {
  it('shows the rows and the given total when ready (the total defaults to the row count)', () => {
    expect(showcaseListView(ShowcaseState.Ready, ROWS, 42)).toEqual({ rows: ROWS, total: 42, loading: false, error: null });
    expect(showcaseListView(ShowcaseState.Ready, ROWS).total).toBe(ROWS.length);
  });

  it('shows no rows while loading, with the flag set', () => {
    expect(showcaseListView(ShowcaseState.Loading, ROWS)).toEqual({ rows: [], total: 0, loading: true, error: null });
  });

  it('shows no rows when empty', () => {
    expect(showcaseListView(ShowcaseState.Empty, ROWS)).toEqual({ rows: [], total: 0, loading: false, error: null });
  });

  it('shows the fixed API error in the error state', () => {
    expect(showcaseListView(ShowcaseState.Error, ROWS)).toEqual({ rows: [], total: 0, loading: false, error: SHOWCASE_API_ERROR });
  });
});

describe('SHOWCASE_API_ERROR', () => {
  it('is an upstream outage with a stable reference code', () => {
    expect(SHOWCASE_API_ERROR.code).toBe(ApiErrorCodes.upstream);
    expect(SHOWCASE_API_ERROR.referenceCode).toBe(referenceCodeFrom(SHOWCASE_CORRELATION_ID));
    expect(SHOWCASE_API_ERROR.referenceCode).toBe('7C1E5-A90D3');
  });
});
