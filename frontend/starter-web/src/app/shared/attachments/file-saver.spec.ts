import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FileSaver, OBJECT_URL_RELEASE_DELAY_MS } from './file-saver';

describe('FileSaver', () => {
  const objectUrl = 'blob:test-url';
  let anchor: HTMLAnchorElement;
  let createObjectURL: ReturnType<typeof vi.fn<(blob: Blob | MediaSource) => string>>;
  let revokeObjectURL: ReturnType<typeof vi.fn<(url: string) => void>>;
  const originalCreate = URL.createObjectURL;
  const originalRevoke = URL.revokeObjectURL;

  beforeEach(() => {
    vi.useFakeTimers();
    createObjectURL = vi.fn<(blob: Blob | MediaSource) => string>(() => objectUrl);
    revokeObjectURL = vi.fn<(url: string) => void>();
    URL.createObjectURL = createObjectURL;
    URL.revokeObjectURL = revokeObjectURL;
    const document = TestBed.inject(DOCUMENT);
    anchor = document.createElement('a');
    vi.spyOn(anchor, 'click').mockImplementation(() => undefined);
    vi.spyOn(document, 'createElement').mockReturnValue(anchor);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    URL.createObjectURL = originalCreate;
    URL.revokeObjectURL = originalRevoke;
  });

  it('clicks a transient download link and releases the URL after the delay', () => {
    const blob = new Blob(['x']);
    TestBed.inject(FileSaver).save(blob, 'report.pdf');

    expect(createObjectURL).toHaveBeenCalledWith(blob);
    expect(anchor.href).toBe(objectUrl);
    expect(anchor.download).toBe('report.pdf');
    expect(anchor.click).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).not.toHaveBeenCalled();

    vi.advanceTimersByTime(OBJECT_URL_RELEASE_DELAY_MS);
    expect(revokeObjectURL).toHaveBeenCalledWith(objectUrl);
  });
});
