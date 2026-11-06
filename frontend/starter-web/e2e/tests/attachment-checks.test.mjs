// @vitest-environment node
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import {
  isCrossOrigin,
  judgeRefusal,
  oversizedPdf,
  oversizedPdfName,
  referenceCodeIn,
  sameBytes,
  samplePdf,
  samplePdfName,
  ticketHeaderName,
} from '../lib/attachment-checks.mjs';
import { ALL_FLOWS, ATTACHMENT_MAX_FILE_BYTES, FILE_TOO_LARGE_CODE, FileTooLargeTexts, FlowNames, PDF_HEADER } from '../lib/flow-constants.mjs';

const STAMP = '2026-11-06T10-20-30-123Z';
const CODE = '9C1E5-B7A20';
const SERVICE_SETTINGS = new URL('../../../../services/attachment-service/src/Codelab.Attachments.Api/appsettings.json', import.meta.url);

function bundle(language) {
  return JSON.parse(readFileSync(new URL(`../../src/assets/i18n/${language}.json`, import.meta.url), 'utf8'));
}

describe('attachments flow data', () => {
  it('names the kept and the refused files after the run token and the language', () => {
    expect(samplePdfName(STAMP)).toBe('e2e-6t102030123z.pdf');
    expect(oversizedPdfName(STAMP, 'ar')).toBe('e2e-6t102030123z-oversized-ar.pdf');
    expect(oversizedPdfName(STAMP, 'en')).not.toBe(oversizedPdfName(STAMP, 'ar'));
  });

  it('builds a small PDF that starts with the PDF header and differs per file name', () => {
    const pdf = samplePdf('a.pdf');
    expect(pdf.subarray(0, PDF_HEADER.length).toString('utf8')).toBe(PDF_HEADER);
    expect(sameBytes(pdf, samplePdf('b.pdf'))).toBe(false);
    expect(pdf.length).toBeLessThan(ATTACHMENT_MAX_FILE_BYTES);
  });

  it('builds an oversized PDF of exactly one byte over the limit, header first', () => {
    const pdf = oversizedPdf(100);
    expect(pdf.length).toBe(101);
    expect(pdf.subarray(0, PDF_HEADER.length).toString('utf8')).toBe(PDF_HEADER);
  });

  it('refuses a limit that is not a whole number or is smaller than the header', () => {
    expect(() => oversizedPdf(1.5)).toThrow(RangeError);
    expect(() => oversizedPdf(1)).toThrow(RangeError);
  });

  it('compares bytes, not lengths', () => {
    expect(sameBytes(Buffer.from('abc'), new Uint8Array([97, 98, 99]))).toBe(true);
    expect(sameBytes(Buffer.from('abc'), Buffer.from('abd'))).toBe(false);
  });

  it('is part of the flow list', () => {
    expect(ALL_FLOWS).toContain(FlowNames.Attachments);
  });
});

describe('attachments flow checks', () => {
  it('finds the upload-ticket header in any case and nothing else', () => {
    expect(ticketHeaderName({ 'content-type': 'multipart/form-data', 'x-starter-upload-ticket': 't' })).toBe('x-starter-upload-ticket');
    expect(ticketHeaderName({ 'X-Fork-Upload-Ticket': 't' })).toBe('X-Fork-Upload-Ticket');
    expect(ticketHeaderName({ authorization: 'Bearer t' })).toBeNull();
    expect(ticketHeaderName(undefined)).toBeNull();
  });

  it('reads a server reference code, also inside Arabic text with direction isolates', () => {
    expect(referenceCodeIn(`الرمز المرجعي: ⁨${CODE}⁩`)).toBe(CODE);
    expect(referenceCodeIn('9c1e5-b7a20 or 9C1E5B7A20')).toBeNull();
    expect(referenceCodeIn(null)).toBeNull();
  });

  it('tells the attachment service origin from the app origin', () => {
    expect(isCrossOrigin('http://localhost:5185/api/files', 'http://localhost:4280')).toBe(true);
    expect(isCrossOrigin('http://localhost:4280/api/files', 'http://localhost:4280/')).toBe(false);
  });

  it('passes a refusal that shows the expected text and quotes the same reference code in the banner', () => {
    expect(
      judgeRefusal({ rowErrorText: ` ${FileTooLargeTexts.en} `, rowReferenceText: CODE, bannerText: `${FileTooLargeTexts.en} Reference code: ${CODE}`, expectedText: FileTooLargeTexts.en }),
    ).toEqual({ ok: true, referenceCode: CODE });
  });

  it('fails a refusal with the wrong text, no reference code or a banner without the code', () => {
    const base = { rowErrorText: FileTooLargeTexts.ar, rowReferenceText: CODE, bannerText: CODE, expectedText: FileTooLargeTexts.ar };
    expect(judgeRefusal({ ...base, rowErrorText: FileTooLargeTexts.en }).ok).toBe(false);
    expect(judgeRefusal({ ...base, rowReferenceText: null }).reason).toContain('no reference code');
    expect(judgeRefusal({ ...base, bannerText: 'something went wrong' }).reason).toContain('does not quote');
  });
});

describe('attachments flow constants mirror their sources', () => {
  it('expects the too-large text of each translation bundle', () => {
    expect(FileTooLargeTexts.en).toBe(bundle('en').errors[FILE_TOO_LARGE_CODE]);
    expect(FileTooLargeTexts.ar).toBe(bundle('ar').errors[FILE_TOO_LARGE_CODE]);
  });

  it('uses the attachment service MaxFileBytes', () => {
    const settings = JSON.parse(readFileSync(SERVICE_SETTINGS, 'utf8'));
    expect(ATTACHMENT_MAX_FILE_BYTES).toBe(settings.Codelab.Security.Uploads.MaxFileBytes);
  });
});
