import { describe, expect, it } from 'vitest';
import { acceptAttributeFor, extensionOf, hasAllowedExtension } from './attachment-files';

const ALLOWED = ['.pdf', '.png', '.jpg', '.jpeg'];

describe('attachment file names', () => {
  it('builds an accept attribute', () => {
    expect(acceptAttributeFor(['.PDF', '.png'])).toBe('.pdf,.png');
    expect(acceptAttributeFor([])).toBe('');
  });

  it('reads the lowercase extension, or null when there is none', () => {
    expect(extensionOf('REPORT.PDF')).toBe('.pdf');
    expect(extensionOf('archive.tar.gz')).toBe('.gz');
    for (const name of ['README', '.env', 'name.', '']) expect(extensionOf(name)).toBeNull();
  });

  it('accepts an allowed extension in any case and refuses the rest', () => {
    expect(hasAllowedExtension('scan.JPEG', ALLOWED)).toBe(true);
    expect(hasAllowedExtension('report.pdf.exe', ALLOWED)).toBe(false);
    expect(hasAllowedExtension('report', ALLOWED)).toBe(false);
    expect(hasAllowedExtension('report.pdf', ['.PDF'])).toBe(true);
  });
});
