// Reads the .eml files the API's FileDrop e-mail sender writes (pure: message text in, fields out). Handles what
// System.Net.Mail writes: RFC 2047 encoded headers (B and Q), folded header lines, and base64 or quoted-printable
// bodies, single part or multipart.

const HEADER_BODY_SEPARATOR = /\r?\n\r?\n/;
const LINE_BREAK = /\r?\n/;
const FOLDED_LINE = /^[ \t]/;
const ENCODED_WORD = /=\?([^?]+)\?([bBqQ])\?([^?]*)\?=/g;
const ENCODED_WORD_GAP = /\?=\s+=\?/g;
const SOFT_BREAK = /=\r?\n/g;
const QP_ESCAPE = /^=([0-9A-Fa-f]{2})$/;
const QP_ESCAPE_LENGTH = 3;
const BOUNDARY = /boundary="?([^";]+)"?/i;
const HREF = /href="([^"]+)"/gi;
const ADDRESS = /<([^>]+)>|([^\s<>,"]+@[^\s<>,"]+)/;
const Q_UNDERSCORE = /_/g;
const BASE64 = 'base64';
const QUOTED_PRINTABLE = 'quoted-printable';
const HEX = 16;

function decodeBytes(bytes, charset) {
  try {
    return new TextDecoder(charset.toLowerCase()).decode(bytes);
  } catch {
    return new TextDecoder().decode(bytes);
  }
}

function quotedPrintableBytes(text) {
  const raw = text.replace(SOFT_BREAK, '');
  const bytes = [];
  for (let index = 0; index < raw.length; index += 1) {
    const match = raw.slice(index, index + QP_ESCAPE_LENGTH).match(QP_ESCAPE);
    if (match) {
      bytes.push(Number.parseInt(match[1], HEX));
      index += QP_ESCAPE_LENGTH - 1;
    } else {
      bytes.push(...Buffer.from(raw[index], 'utf8'));
    }
  }
  return Uint8Array.from(bytes);
}

/** Decodes RFC 2047 encoded words in a header value (adjacent encoded words join without the gap). */
export function decodeHeaderValue(value) {
  return value.replace(ENCODED_WORD_GAP, '?==?').replace(ENCODED_WORD, (_, charset, encoding, text) =>
    encoding.toUpperCase() === 'B'
      ? decodeBytes(Buffer.from(text, BASE64), charset)
      : decodeBytes(quotedPrintableBytes(text.replace(Q_UNDERSCORE, ' ')), charset),
  );
}

/** Header lines (unfolded) into a Map of lower-case name to raw value (the first occurrence wins). */
export function parseHeaders(headerText) {
  const headers = new Map();
  const lines = [];
  for (const line of headerText.split(LINE_BREAK)) {
    if (FOLDED_LINE.test(line) && lines.length > 0) lines[lines.length - 1] += ` ${line.trim()}`;
    else lines.push(line);
  }
  for (const line of lines) {
    const colon = line.indexOf(':');
    if (colon <= 0) continue;
    const name = line.slice(0, colon).trim().toLowerCase();
    if (!headers.has(name)) headers.set(name, line.slice(colon + 1).trim());
  }
  return headers;
}

function charsetOf(contentType) {
  return contentType?.match(/charset="?([^";]+)"?/i)?.[1] ?? 'utf-8';
}

function decodePart(headers, body) {
  const encoding = (headers.get('content-transfer-encoding') ?? '').toLowerCase();
  const charset = charsetOf(headers.get('content-type'));
  if (encoding === BASE64) return decodeBytes(Buffer.from(body.replace(/\s+/g, ''), BASE64), charset);
  if (encoding === QUOTED_PRINTABLE) return decodeBytes(quotedPrintableBytes(body), charset);
  return body;
}

function splitOnce(text) {
  const match = text.match(HEADER_BODY_SEPARATOR);
  if (!match) return { head: text, body: '' };
  return { head: text.slice(0, match.index), body: text.slice(match.index + match[0].length) };
}

/** The decoded body text (every text part of a multipart message, joined). */
function bodyOf(headers, body) {
  const boundary = headers.get('content-type')?.match(BOUNDARY)?.[1];
  if (!boundary) return decodePart(headers, body);
  return body
    .split(`--${boundary}`)
    .slice(1)
    .filter((part) => !part.startsWith('--'))
    .map((part) => {
      const { head, body: partBody } = splitOnce(part.replace(/^\r?\n/, ''));
      return bodyOf(parseHeaders(head), partBody);
    })
    .join('\n');
}

/** Parses one .eml text: { to (lower-case address or null), subject, body }. */
export function parseEml(text) {
  const { head, body } = splitOnce(text);
  const headers = parseHeaders(head);
  const toMatch = (headers.get('to') ?? '').match(ADDRESS);
  const to = toMatch ? (toMatch[1] ?? toMatch[2]).trim().toLowerCase() : null;
  return { to, subject: decodeHeaderValue(headers.get('subject') ?? ''), body: bodyOf(headers, body) };
}

/** Every href in an HTML body, entity &amp; decoded. */
export function linksOf(html) {
  return [...html.matchAll(HREF)].map((match) => match[1].replaceAll('&amp;', '&'));
}

/** The first link in a mail body whose path is the given app path (e.g. /activate), or null. */
export function findLink(mail, appPath) {
  return (
    linksOf(mail.body).find((href) => {
      try {
        return new URL(href).pathname === appPath;
      } catch {
        return false;
      }
    }) ?? null
  );
}

/**
 * The newest mail (by the given modified time) that matches: written at or after notBeforeMs, to the address
 * (case-insensitive) when given, and whose subject contains the text (or one of the texts) when given.
 * mails: [{ mtimeMs, mail }]. Returns the mail or null.
 */
export function pickMail(mails, { to = null, subjectIncludes = null, notBeforeMs = 0 } = {}) {
  const wanted = to?.toLowerCase() ?? null;
  const wantedTexts = subjectIncludes === null ? [] : [subjectIncludes].flat();
  return (
    [...mails]
      .filter((entry) => entry.mtimeMs >= notBeforeMs)
      .filter((entry) => wanted === null || entry.mail.to === wanted)
      .filter((entry) => wantedTexts.length === 0 || wantedTexts.some((text) => entry.mail.subject.includes(text)))
      .sort((left, right) => right.mtimeMs - left.mtimeMs)[0]?.mail ?? null
  );
}
