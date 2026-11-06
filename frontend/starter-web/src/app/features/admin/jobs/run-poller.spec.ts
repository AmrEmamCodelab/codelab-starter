import { Injector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RUN_POLL_INTERVAL_MS, RUN_POLL_MAX_ROUNDS, RunPoller } from './run-poller';

describe('RunPoller', () => {
  let reload: ReturnType<typeof vi.fn<() => void>>;
  let poller: RunPoller;

  beforeEach(() => {
    vi.useFakeTimers();
    reload = vi.fn<() => void>();
    TestBed.configureTestingModule({});
    poller = runInInjectionContext(TestBed.inject(Injector), () => new RunPoller(reload));
  });

  afterEach(() => vi.useRealTimers());

  it('reloads after the interval while a run is active', () => {
    poller.follow(true);
    vi.advanceTimersByTime(RUN_POLL_INTERVAL_MS - 1);
    expect(reload).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('stops when nothing is active', () => {
    poller.follow(true);
    poller.follow(false);
    vi.advanceTimersByTime(RUN_POLL_INTERVAL_MS * 2);
    expect(reload).not.toHaveBeenCalled();
  });

  it('gives up after the maximum rounds, until restarted', () => {
    for (let round = 0; round < RUN_POLL_MAX_ROUNDS + 2; round += 1) {
      poller.follow(true);
      vi.advanceTimersByTime(RUN_POLL_INTERVAL_MS);
    }
    expect(reload).toHaveBeenCalledTimes(RUN_POLL_MAX_ROUNDS);
    poller.restart();
    poller.follow(true);
    vi.advanceTimersByTime(RUN_POLL_INTERVAL_MS);
    expect(reload).toHaveBeenCalledTimes(RUN_POLL_MAX_ROUNDS + 1);
  });

  it('cancels a scheduled reload on restart and when destroyed', () => {
    poller.follow(true);
    poller.restart();
    vi.advanceTimersByTime(RUN_POLL_INTERVAL_MS);
    expect(reload).not.toHaveBeenCalled();
    poller.follow(true);
    TestBed.resetTestingModule();
    vi.advanceTimersByTime(RUN_POLL_INTERVAL_MS);
    expect(reload).not.toHaveBeenCalled();
  });
});
