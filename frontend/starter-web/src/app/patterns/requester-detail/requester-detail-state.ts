import type { ApiError } from '@codelab/ng-security';

/** What the requester detail page shows. */
export const DetailViewState = {
  /** The record is loading (and none is shown yet). */
  Loading: 'loading',
  /** The load failed: the error with its reference code and Retry. */
  Error: 'error',
  /** The record is shown. */
  Ready: 'ready',
} as const;
export type DetailViewState = (typeof DetailViewState)[keyof typeof DetailViewState];

/** The inputs that decide the view state. */
export interface DetailViewInputs {
  readonly loading: boolean;
  readonly error: ApiError | null;
}

/**
 * The view state: a load error wins (Retry is the way out), then loading, else the record. A reload after an error
 * shows loading again only once the screen clears the error, so the reference code stays readable until then.
 */
export function detailViewState({ loading, error }: DetailViewInputs): DetailViewState {
  if (error) return DetailViewState.Error;
  if (loading) return DetailViewState.Loading;
  return DetailViewState.Ready;
}
