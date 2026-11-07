import { TestBed } from '@angular/core/testing';
import { parseApiError } from '@codelab/ng-security';
import { beforeEach, describe, expect, it } from 'vitest';
import { ErrorDialogStore } from './error-dialog.store';

const TRACE_ID = 'd1e8a7c45b2f4e6a9c3b0f7d5a2e8c61';

describe('ErrorDialogStore', () => {
  let store: ErrorDialogStore;

  beforeEach(() => {
    store = TestBed.inject(ErrorDialogStore);
  });

  it('starts closed with no reference code', () => {
    expect(store.current()).toBeNull();
    expect(store.messageKey()).toBeNull();
    expect(store.referenceCode()).toBeNull();
    expect(store.correlationId()).toBeNull();
    expect(store.hasReferenceCode()).toBe(false);
  });

  it('keeps the reference code and the correlation id of the failure it shows', () => {
    store.show(parseApiError({ status: 500, error: { code: 'INTERNAL_ERROR', correlationId: TRACE_ID, referenceCode: 'D1E8A-7C45B' } }));
    expect(store.messageKey()).toBe('errors.INTERNAL_ERROR');
    expect(store.referenceCode()).toBe('D1E8A-7C45B');
    expect(store.correlationId()).toBe(TRACE_ID);
    expect(store.hasReferenceCode()).toBe(true);
  });

  it('has no reference code for a failure without one', () => {
    store.show(parseApiError({ status: 0 }));
    expect(store.messageKey()).not.toBeNull();
    expect(store.referenceCode()).toBeNull();
    expect(store.hasReferenceCode()).toBe(false);
  });

  it('replaces an earlier failure with a later one', () => {
    store.show(parseApiError({ status: 500, error: { correlationId: TRACE_ID } }));
    store.show(parseApiError({ status: 409, error: { code: 'CONFLICT' } }));
    expect(store.referenceCode()).toBeNull();
  });

  it('forgets the failure on dismiss', () => {
    store.show(parseApiError({ status: 500, error: { correlationId: TRACE_ID } }));
    store.dismiss();
    expect(store.current()).toBeNull();
    expect(store.referenceCode()).toBeNull();
    expect(store.hasReferenceCode()).toBe(false);
  });
});
