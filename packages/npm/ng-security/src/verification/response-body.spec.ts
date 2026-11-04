import { describe, expect, it } from 'vitest';
import { bufferOf } from '../testing/test-support.ts';
import { decodeForDelivery } from './response-body.ts';

describe('decodeForDelivery', () => {
  it('hands back the bytes themselves for arraybuffer', () => {
    const bytes = bufferOf('x');
    expect(decodeForDelivery(bytes, 'arraybuffer', '').value).toBe(bytes);
  });

  it('wraps the bytes in a Blob with the received type', async () => {
    const blob = decodeForDelivery(bufferOf('abc'), 'blob', 'text/csv').value as Blob;
    expect(blob.type).toBe('text/csv');
    expect(await blob.text()).toBe('abc');
    expect((decodeForDelivery(null, 'blob', '').value as Blob).size).toBe(0);
  });

  it('decodes UTF-8 text, Arabic included', () => {
    expect(decodeForDelivery(bufferOf('طلب'), 'text', 'text/plain').value).toBe('طلب');
    expect(decodeForDelivery(null, 'text', '').value).toBe('');
  });

  it('parses JSON and strips the XSSI prefix as Angular does', () => {
    expect(decodeForDelivery(bufferOf('{"a":1}'), 'json', '').value).toEqual({ a: 1 });
    expect(decodeForDelivery(bufferOf(")]}',\n[1]"), 'json', '').value).toEqual([1]);
  });

  it('delivers null for an empty JSON body', () => {
    const decoded = decodeForDelivery(bufferOf(''), 'json', '');
    expect(decoded.value).toBeNull();
    expect(decoded.jsonParseError).toBeNull();
  });

  it('reports a JSON parse failure with the original text instead of half-decoding', () => {
    const decoded = decodeForDelivery(bufferOf('<html>'), 'json', 'text/html');
    expect(decoded.jsonParseError).toBeInstanceOf(SyntaxError);
    expect(decoded.text).toBe('<html>');
    expect(decoded.value).toBe('<html>');
  });
});
