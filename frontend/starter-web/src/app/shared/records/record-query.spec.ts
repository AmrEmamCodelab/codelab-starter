import { describe, expect, it } from 'vitest';
import { mineParams, oversightParams, queryParams, queueParams } from './record-query';
import { QueueView } from './records.models';

describe('record query params', () => {
  it('keeps set values in order and leaves out null, undefined and blank text', () => {
    const params = queryParams([['a', 'x'], ['b', null], ['c', undefined], ['d', '  '], ['e', 0], ['f', ' y ']]);
    expect(params.toString()).toBe('a=x&e=0&f=y');
  });

  it('sends nothing for an empty mine or oversight query', () => {
    expect(mineParams().toString()).toBe('');
    expect(oversightParams().toString()).toBe('');
  });

  it('always sends the queue view', () => {
    expect(queueParams({ view: QueueView.Mine }).toString()).toBe('view=mine');
    expect(queueParams({ view: QueueView.All, number: 'TR-1', submittedTo: '2026-11-06' }).toString())
      .toBe('view=all&number=TR-1&submittedTo=2026-11-06');
  });
});
