import { ApiErrorCodes, HttpStatus, parseApiError, type ApiError } from '@codelab/ng-security';

/** The data states a pattern page can be switched between, so every pattern shows its loading, empty and error UI. */
export const ShowcaseState = {
  Ready: 'ready',
  Loading: 'loading',
  Empty: 'empty',
  Error: 'error',
} as const;

export type ShowcaseState = (typeof ShowcaseState)[keyof typeof ShowcaseState];

/** A fixed W3C trace id, so the error state shows the same reference code on every run. */
export const SHOWCASE_CORRELATION_ID = '7c1e5a90d3b24f6e8a0b2c4d6e8f1a3b';

/** The API error the error state shows: an upstream outage with a reference code, as a real refusal parses. */
export const SHOWCASE_API_ERROR: ApiError = parseApiError({
  status: HttpStatus.serviceUnavailable,
  error: { code: ApiErrorCodes.upstream, correlationId: SHOWCASE_CORRELATION_ID },
});

/** What a list pattern binds for one state: the rows, the total, the loading flag and the error. */
export interface ShowcaseListView<T> {
  readonly rows: readonly T[];
  readonly total: number;
  readonly loading: boolean;
  readonly error: ApiError | null;
}

/**
 * The list inputs for a state: `Ready` shows the fixture rows and total, `Loading` keeps no rows and sets the flag,
 * `Empty` shows no rows, and `Error` shows no rows with {@link SHOWCASE_API_ERROR}.
 */
export function showcaseListView<T>(state: ShowcaseState, rows: readonly T[], total: number = rows.length): ShowcaseListView<T> {
  const none = { rows: [] as readonly T[], total: 0, loading: false, error: null };
  switch (state) {
    case ShowcaseState.Ready:
      return { ...none, rows, total };
    case ShowcaseState.Loading:
      return { ...none, loading: true };
    case ShowcaseState.Error:
      return { ...none, error: SHOWCASE_API_ERROR };
    default:
      return none;
  }
}
