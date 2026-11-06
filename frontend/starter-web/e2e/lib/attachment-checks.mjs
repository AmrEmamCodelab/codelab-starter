// The data and pass rules of the attachments flow (pure: the run stamp, sizes, headers and page text in; file bytes,
// names and verdicts out). flows/attachments.mjs is the only caller that drives the browser.
import {
  AttachmentFileNames,
  PDF_EXTENSION,
  PDF_HEADER,
  PDF_PADDING_BYTE,
  PDF_TRAILER,
  REFERENCE_CODE_PATTERN,
  UPLOAD_TICKET_HEADER_PATTERN,
} from './flow-constants.mjs';
import { runToken } from './flow-checks.mjs';

const ENCODING = 'utf8';

/** The kept file's name of this run: e2e-<token>.pdf. */
export function samplePdfName(runStamp) {
  return `${AttachmentFileNames.Prefix}${runToken(runStamp)}${PDF_EXTENSION}`;
}

/** The refused file's name of this run in one language: e2e-<token>-oversized-<lang>.pdf. */
export function oversizedPdfName(runStamp, language) {
  return `${AttachmentFileNames.Prefix}${runToken(runStamp)}${AttachmentFileNames.OversizedInfix}${language}${PDF_EXTENSION}`;
}

/** A small PDF the service's signature check accepts; its body names the file, so each run's bytes differ. */
export function samplePdf(fileName) {
  return Buffer.from(`${PDF_HEADER}% ${fileName}\n${PDF_TRAILER}`, ENCODING);
}

/** A PDF of exactly maxFileBytes + 1 bytes: a valid header, then padding. Only its size is wrong. */
export function oversizedPdf(maxFileBytes) {
  if (!Number.isSafeInteger(maxFileBytes) || maxFileBytes < PDF_HEADER.length) {
    throw new RangeError(`maxFileBytes must be a whole number of at least ${PDF_HEADER.length}; got ${maxFileBytes}.`);
  }
  const content = Buffer.alloc(maxFileBytes + 1, PDF_PADDING_BYTE);
  content.write(PDF_HEADER, 0, ENCODING);
  return content;
}

/** Whether two byte arrays hold the same bytes (the downloaded file against the uploaded one). */
export function sameBytes(left, right) {
  return Buffer.from(left).equals(Buffer.from(right));
}

/** The name of the upload-ticket header among request headers (any case), or null when none is there. */
export function ticketHeaderName(headers) {
  return Object.keys(headers ?? {}).find((name) => UPLOAD_TICKET_HEADER_PATTERN.test(name)) ?? null;
}

/** The first server reference code (XXXXX-XXXXX, upper-case hex) in a text, or null. */
export function referenceCodeIn(text) {
  return String(text ?? '').match(REFERENCE_CODE_PATTERN)?.[0] ?? null;
}

/** Whether a request went to another origin than the app (the browser talks to the attachment service directly). */
export function isCrossOrigin(requestUrl, appBaseUrl) {
  return new URL(requestUrl).origin !== new URL(appBaseUrl).origin;
}

/**
 * The refusal verdict of one language: the row's error text is the expected text, the row shows a reference code and
 * the banner quotes the same code. Returns { ok: true, referenceCode } or { ok: false, reason }.
 */
export function judgeRefusal({ rowErrorText, rowReferenceText, bannerText, expectedText }) {
  const shown = String(rowErrorText ?? '').trim();
  if (shown !== expectedText) return { ok: false, reason: `the row says "${shown}", expected "${expectedText}"` };
  const referenceCode = referenceCodeIn(rowReferenceText);
  if (referenceCode === null) return { ok: false, reason: `the row shows no reference code (got "${String(rowReferenceText ?? '').trim()}")` };
  if (!String(bannerText ?? '').includes(referenceCode)) return { ok: false, reason: `the banner does not quote ${referenceCode} (got "${String(bannerText ?? '').trim()}")` };
  return { ok: true, referenceCode };
}
