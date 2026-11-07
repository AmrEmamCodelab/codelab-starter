import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { OpsApiClient } from '../api/ops-api.client';
import { FakeOpsApiClient } from '../testing/ops.testing';
import { OperatorStore } from './operator.store';

describe('OperatorStore', () => {
  let client: FakeOpsApiClient;

  beforeEach(() => {
    client = new FakeOpsApiClient();
    TestBed.configureTestingModule({ providers: [{ provide: OpsApiClient, useValue: client }] });
  });

  it('loads the signed-in operator from the BFF', () => {
    const store = TestBed.inject(OperatorStore);
    expect(store.name()).toBeNull();
    store.load();
    client.answer('me', { name: 'CORP\\operator1', groups: ['ops'] });
    expect(store.name()).toBe('CORP\\operator1');
    expect(store.operator()?.groups).toEqual(['ops']);
  });

  it('keeps the failure for the screen (the interceptor handles 401 and 403)', () => {
    const store = TestBed.inject(OperatorStore);
    store.load();
    client.fail('me', 500, { code: 'INTERNAL_ERROR' });
    expect(store.name()).toBeNull();
    expect(store.error()?.code).toBe('INTERNAL_ERROR');
  });
});
