// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { decodeHeaderValue, findLink, linksOf, parseEml, parseHeaders, pickMail } from '../lib/mail.mjs';

const ARABIC_SUBJECT = 'تفعيل حسابك';
const b64 = (text) => Buffer.from(text, 'utf8').toString('base64');

function eml({ to = 'User <New.User@Example.test>', subject = `=?utf-8?B?${b64(ARABIC_SUBJECT)}?=`, encoding = 'base64', body }) {
  return [
    'X-Sender: no-reply@example.test',
    'MIME-Version: 1.0',
    'From: "Starter" <no-reply@example.test>',
    `To: ${to}`,
    `Subject: ${subject}`,
    'Content-Type: text/html; charset=utf-8',
    `Content-Transfer-Encoding: ${encoding}`,
    '',
    body,
  ].join('\r\n');
}

describe('decodeHeaderValue', () => {
  it('decodes B and Q encoded words and joins adjacent ones', () => {
    expect(decodeHeaderValue(`=?utf-8?B?${b64(ARABIC_SUBJECT)}?=`)).toBe(ARABIC_SUBJECT);
    expect(decodeHeaderValue('=?utf-8?Q?New_contact_=C3=A9?=')).toBe('New contact é');
    expect(decodeHeaderValue(`=?utf-8?B?${b64('CU-')}?= =?utf-8?B?${b64('2026-001')}?=`)).toBe('CU-2026-001');
    expect(decodeHeaderValue('Plain subject')).toBe('Plain subject');
  });
});

describe('parseHeaders', () => {
  it('unfolds continuation lines and keeps the first occurrence', () => {
    const headers = parseHeaders('Subject: one\r\n two\r\nSubject: later\r\nTo: a@b.test');
    expect(headers.get('subject')).toBe('one two');
    expect(headers.get('to')).toBe('a@b.test');
  });
});

describe('parseEml', () => {
  it('reads the recipient, the decoded subject and a base64 HTML body', () => {
    const mail = parseEml(eml({ body: b64('<p><a href="http://localhost:4280/activate#ticket=abc&amp;x=1">Go</a></p>') }));
    expect(mail.to).toBe('new.user@example.test');
    expect(mail.subject).toBe(ARABIC_SUBJECT);
    expect(findLink(mail, '/activate')).toBe('http://localhost:4280/activate#ticket=abc&x=1');
  });

  it('decodes a quoted-printable body with soft line breaks and UTF-8 bytes', () => {
    const mail = parseEml(eml({ to: 'a@b.test', subject: 'Test', encoding: 'quoted-printable', body: '<p>=D9=85=D8=B1=D8=AD=D8=A8=D8=A7 hel=\r\nlo</p>' }));
    expect(mail.body).toBe('<p>مرحبا hello</p>');
  });

  it('joins the text parts of a multipart message', () => {
    const text = [
      'To: a@b.test',
      'Subject: Multi',
      'Content-Type: multipart/alternative; boundary="--b1"',
      '',
      '----b1',
      'Content-Type: text/plain; charset=utf-8',
      'Content-Transfer-Encoding: base64',
      '',
      b64('plain part'),
      '----b1',
      'Content-Type: text/html; charset=utf-8',
      '',
      '<a href="http://h/x">x</a>',
      '----b1--',
    ].join('\r\n');
    const mail = parseEml(text);
    expect(mail.body).toContain('plain part');
    expect(linksOf(mail.body)).toEqual(['http://h/x']);
  });
});

describe('findLink', () => {
  it('matches the path exactly and skips invalid hrefs', () => {
    const mail = { body: '<a href="mailto:x">m</a><a href="http://h/activated">no</a><a href="http://h/activate?t=1">yes</a>' };
    expect(findLink(mail, '/activate')).toBe('http://h/activate?t=1');
    expect(findLink({ body: '<a href="relative">r</a>' }, '/activate')).toBeNull();
  });
});

describe('pickMail', () => {
  const entry = (mtimeMs, to, subject) => ({ mtimeMs, mail: { to, subject, body: '' } });
  const mails = [entry(10, 'a@b.test', 'Test notification'), entry(30, 'a@b.test', 'إشعار تجريبي'), entry(20, 'c@d.test', 'New contact CU-2026-001')];

  it('returns the newest match by recipient, subject text (one of several) and time', () => {
    expect(pickMail(mails, { to: 'A@B.test' }).subject).toBe('إشعار تجريبي');
    expect(pickMail(mails, { subjectIncludes: ['Test notification', 'إشعار تجريبي'], notBeforeMs: 0 }).subject).toBe('إشعار تجريبي');
    expect(pickMail(mails, { subjectIncludes: 'CU-2026-001' }).to).toBe('c@d.test');
    expect(pickMail(mails, { to: 'a@b.test', notBeforeMs: 31 })).toBeNull();
    expect(pickMail([], {})).toBeNull();
  });
});
