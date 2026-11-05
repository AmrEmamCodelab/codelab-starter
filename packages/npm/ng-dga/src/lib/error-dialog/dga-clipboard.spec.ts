import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { browserClipboard, DGA_CLIPBOARD } from './dga-clipboard';

describe('browserClipboard', () => {
  it('writes through the Clipboard API and reports success', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    await expect(browserClipboard({ writeText }).write('REF-1')).resolves.toBe(true);
    expect(writeText).toHaveBeenCalledWith('REF-1');
  });

  it('reports false when the browser refuses', async () => {
    const writeText = vi.fn().mockRejectedValue(new Error('denied'));
    await expect(browserClipboard({ writeText }).write('REF-1')).resolves.toBe(false);
  });

  it('reports false without a Clipboard API', async () => {
    await expect(browserClipboard(null).write('REF-1')).resolves.toBe(false);
  });
});

describe('DGA_CLIPBOARD', () => {
  it('has a root default', () => {
    expect(typeof TestBed.inject(DGA_CLIPBOARD).write).toBe('function');
  });
});
