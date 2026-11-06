import { HttpErrorResponse } from '@angular/common/http';
import { HttpStatus } from '@codelab/ng-security';
import { Subject, of, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { WholeListState } from './whole-list';

describe('WholeListState', () => {
  it('loads the rows', async () => {
    const state = new WholeListState<string, number>(() => of([1, 2]));
    await state.load('a');
    expect(state.items()).toEqual([1, 2]);
    expect(state.loading()).toBe(false);
    expect(state.error()).toBeNull();
  });

  it('clears the rows and keeps the refusal when a load fails', async () => {
    let fail = false;
    const state = new WholeListState<string, number>(() =>
      fail ? throwError(() => new HttpErrorResponse({ status: HttpStatus.notFound })) : of([1]));
    await state.load('a');
    fail = true;
    await state.load('b');
    expect(state.items()).toEqual([]);
    expect(state.error()?.status).toBe(HttpStatus.notFound);
    expect(state.loading()).toBe(false);
  });

  it('keeps only the answer of the latest request', async () => {
    const slow = new Subject<readonly number[]>();
    const state = new WholeListState<string, number>((query) => (query === 'slow' ? slow : of([2])));
    const first = state.load('slow');
    await state.load('fast');
    slow.next([1]);
    slow.complete();
    await first;
    expect(state.items()).toEqual([2]);
    expect(state.loading()).toBe(false);
  });

  it('rethrows a programming error instead of turning it into a message', async () => {
    const bug = new TypeError('bug');
    const state = new WholeListState<string, number>(() => throwError(() => bug));
    await expect(state.load('a')).rejects.toBe(bug);
  });
});
