import { Subject, of, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { RemoteResource } from './remote-resource';

describe('RemoteResource', () => {
  it('tracks loading and data', () => {
    const resource = new RemoteResource<number>();
    const source = new Subject<number>();
    resource.load(source);
    expect(resource.loading()).toBe(true);
    source.next(7);
    expect(resource.loading()).toBe(false);
    expect(resource.data()).toBe(7);
    expect(resource.error()).toBeNull();
  });

  it('parses a problem+json failure with its reference code and keeps the last data', () => {
    const resource = new RemoteResource<number>();
    resource.load(of(1));
    resource.load(throwError(() => ({ status: 502, error: { code: 'GATEWAY_UPSTREAM_REJECTED', correlationId: '7d2c1e9a04b65f38c1a9e2d70b4f6a15' } })));
    expect(resource.data()).toBe(1);
    expect(resource.error()).toMatchObject({ status: 502, code: 'GATEWAY_UPSTREAM_REJECTED', referenceCode: '7D2C1-E9A04' });
    expect(resource.loading()).toBe(false);
  });

  it('ignores the answer of a call a newer load replaced', () => {
    const resource = new RemoteResource<string>();
    const old = new Subject<string>();
    resource.load(old);
    resource.load(of('new'));
    old.next('old');
    expect(resource.data()).toBe('new');
  });

  it('cancel ends loading and reset clears everything', () => {
    const resource = new RemoteResource<string>();
    const pending = new Subject<string>();
    resource.load(pending);
    resource.cancel();
    expect(resource.loading()).toBe(false);
    pending.next('late');
    expect(resource.data()).toBeNull();
    resource.load(of('x'));
    resource.reset();
    expect(resource.data()).toBeNull();
    expect(resource.error()).toBeNull();
  });
});
