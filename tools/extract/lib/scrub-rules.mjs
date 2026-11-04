// Built-in scrub rules that are not plain terms: addresses, national-ID-like numbers, internal hosts, LDAP domains
// and the DGA package check (D5: no external DGA npm package). Term rules come from modhs-terms.txt.
import { posix } from 'node:path';

/**
 * @typedef {'content' | 'path' | 'both'} RuleScope
 * @typedef {{
 *   id: string, pattern: RegExp, message: string, scope: RuleScope,
 *   appliesTo?: (path: string) => boolean,
 *   isAllowedMatch?: (matchText: string, lineText: string, index: number) => boolean,
 *   mask?: (matchText: string) => string,
 * }} ScrubRule
 */

export const SCOPE_CONTENT = 'content';
export const SCOPE_PATH = 'path';
export const SCOPE_BOTH = 'both';

// --- IPv4 -------------------------------------------------------------------------------------------------------
const MAX_OCTET = 255;
const ALLOWED_ADDRESSES = new Set(['127.0.0.1', '0.0.0.0']);
// RFC 5737 documentation ranges (TEST-NET-1/2/3): safe in examples by definition.
const DOCUMENTATION_PREFIXES = ['192.0.2.', '198.51.100.', '203.0.113.'];
// Four dotted numbers IMMEDIATELY after a version keyword are a version, not an address:
// <AssemblyVersion>1.0.0.0, FileVersion("1.2.3.4"), "version": "1.0.0.0", Version=4.0.0.0. Only the text right
// before the number counts; "version" elsewhere on the line exempts nothing.
const VERSION_PREFIX = /(?:version|\bver)["']?\s*(?:>|\(|=|:)?\s*["']?\s*$/i;
const VERSION_LOOKBACK_CHARS = 24;

function isVersionNumber(lineText, index) {
  return VERSION_PREFIX.test(lineText.slice(Math.max(0, index - VERSION_LOOKBACK_CHARS), index));
}

function isAllowedAddress(address, lineText, index) {
  const octets = address.split('.').map(Number);
  if (octets.some((octet) => octet > MAX_OCTET)) return true;
  if (ALLOWED_ADDRESSES.has(address)) return true;
  if (DOCUMENTATION_PREFIXES.some((prefix) => address.startsWith(prefix))) return true;
  return isVersionNumber(lineText, index);
}

// --- IPv6 -------------------------------------------------------------------------------------------------------
// Unique-local (fc and fd prefixes), link-local (fe80 to febf) and global unicast (first hextet 2000 to 3fff).
// The documentation range 2001:db8 (RFC 3849) is allowed.
const IPV6_PATTERN = /(?<![\w:.])(?:f[cd][0-9a-f]{2}|fe[89ab][0-9a-f]|[23][0-9a-f]{3}):(?:[0-9a-f]{0,4}:){1,6}[0-9a-f]{0,4}(?![\w:])/gi;
const IPV6_DOCUMENTATION_PREFIX = /^2001:0?db8:/i;
const IPV6_COMPRESSION = '::';
const IPV6_FULL_GROUP_COLONS = 7;

function isAllowedIpv6(address) {
  const colons = address.split(':').length - 1;
  const isAddress = address.includes(IPV6_COMPRESSION) || colons === IPV6_FULL_GROUP_COLONS;
  return !isAddress || IPV6_DOCUMENTATION_PREFIX.test(address);
}

// --- UNC paths and SQL Server instances --------------------------------------------------------------------------
// Two to four backslashes, a host name of two or more characters, then one or two backslashes and a share name that
// ends the token (JSON-escaped paths double every backslash). Device paths start with ? or . and are not matched;
// regex escapes (a one-letter "host" or "share") are not matched either. Hosts under reserved test domains are allowed.
const UNC_PATH_PATTERN = /(?<![\w\\:])\\{2,4}[A-Za-z][\w.-]+\\{1,2}[\w$.-]{2,}(?=[\\/\s"'`,;)]|$)/g;
const ALLOWED_UNC_HOST = /(?:^|\.)(?:example|test|invalid|localhost)(?:\.[a-z]+)?$/i;
const UNC_HOST = /^\\+([^\\]+)/;

function isAllowedUncPath(matchText) {
  return ALLOWED_UNC_HOST.test(UNC_HOST.exec(matchText)?.[1] ?? '');
}

// A named instance after Server= / Data Source= on a real host. Local instances (localhost, 127.0.0.1, .) are fine.
const SQL_INSTANCE_PATTERN = /\b(?:Server|Data Source|Address|Addr)\s*=\s*(?:tcp:)?([A-Za-z0-9.][\w.-]*)\\{1,2}[A-Za-z][\w$]*/gi;
const SQL_HOST = /=\s*(?:tcp:)?([^\\]+)/i;
const LOCAL_SQL_HOSTS = /^(?:localhost|127\.0\.0\.1|\.)$/i;

function isLocalSqlInstance(matchText) {
  return LOCAL_SQL_HOSTS.test((SQL_HOST.exec(matchText)?.[1] ?? '').trim());
}

// --- People: e-mail addresses and Saudi mobile numbers -----------------------------------------------------------
const EMAIL_PATTERN = /(?<![\w.%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}(?![\w-])/g;
// Reserved documentation and test domains (RFC 2606 / 6761) are not people.
const ALLOWED_EMAIL_DOMAIN = /(?:^|\.)(?:example\.(?:com|org|net)|example|test|invalid|localhost)$/i;
const EMAIL_DOMAIN_SEPARATOR = '@';
// +966 5x xxx xxxx, 00966..., 05xxxxxxxx (digits may be grouped by spaces or dashes).
const SAUDI_MOBILE_PATTERN = /(?<![\w+/.-])(?:(?:\+|00)966[\s-]?|0)5\d(?:[\s-]?\d){7}(?![\w-]|\.\d)/g;

function isAllowedEmail(address) {
  return ALLOWED_EMAIL_DOMAIN.test(address.split(EMAIL_DOMAIN_SEPARATOR).at(-1));
}

// --- Saudi national-ID-like numbers -----------------------------------------------------------------------------
// 10 digits starting 1 (citizen) or 2 (resident), not part of a longer token, base64 run or decimal.
const NATIONAL_ID_PATTERN = /(?<![\w+/.-])[12]\d{9}(?![\w+/-]|\.\d)/g;
const NATIONAL_ID_VISIBLE_DIGITS = 2;

function maskNationalId(value) {
  const hidden = value.length - (NATIONAL_ID_VISIBLE_DIGITS * 2);
  return `${value.slice(0, NATIONAL_ID_VISIBLE_DIGITS)}${'*'.repeat(hidden)}${value.slice(-NATIONAL_ID_VISIBLE_DIGITS)}`;
}

// --- Internal host names ----------------------------------------------------------------------------------------
// Private-use and government suffixes that only ever name a real environment. A suffix followed by ".<letter>"
// is a file name (appsettings.local.json), not a host.
// Every .sa host is denied (organisations, hospitals, ministries) unless it is on ALLOWED_HOSTS.
const INTERNAL_HOST_PATTERN = /\b(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+(?:local|lan|internal|intranet|corp|home\.arpa|sa)(?![\w-]|\.[a-z])/gi;
// Public, documentation-safe names under the denied suffixes.
const ALLOWED_HOSTS = new Set(['dga.gov.sa', 'www.dga.gov.sa', 'example.gov.sa', 'example.sa', 'example.com.sa']);
// LDAP distinguished-name domain components other than documentation names reveal a real directory.
const LDAP_DOMAIN_PATTERN = /\bDC=(?!(?:example|test|invalid|com|org|net)\b)[a-z0-9-]+/gi;

// --- DGA package check ------------------------------------------------------------------------------------------
const DGA_PACKAGE_PATTERN = /@dev-dga|@dga\//g;
const DGA_TARBALL_PATTERN = /dev-dga/gi;
const DGA_CHECKED_FILE_NAMES = new Set([
  'package.json', 'package-lock.json', 'npm-shrinkwrap.json', 'pnpm-lock.yaml', 'yarn.lock', 'angular.json',
]);
const DGA_CHECKED_EXTENSIONS = new Set(['.ts', '.scss', '.css', '.js', '.mjs', '.cjs', '.html']);

function isDgaCheckedFile(path) {
  return DGA_CHECKED_FILE_NAMES.has(posix.basename(path)) || DGA_CHECKED_EXTENSIONS.has(posix.extname(path));
}

/** @returns {ScrubRule[]} the built-in rules, fresh RegExp state per call */
export function builtInScrubRules() {
  return [
    {
      id: 'ipv4',
      pattern: /(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?!\d|\.\d)/g,
      message: 'IPv4 address (only 127.0.0.1, 0.0.0.0 and RFC 5737 documentation ranges are allowed)',
      scope: SCOPE_CONTENT,
      isAllowedMatch: isAllowedAddress,
    },
    {
      id: 'ipv6',
      pattern: IPV6_PATTERN,
      message: 'IPv6 address (only the 2001:db8::/32 documentation range is allowed)',
      scope: SCOPE_CONTENT,
      isAllowedMatch: isAllowedIpv6,
    },
    {
      id: 'unc-path',
      pattern: UNC_PATH_PATTERN,
      message: 'UNC path to a real server share (use a placeholder host such as fileserver.example.test)',
      scope: SCOPE_CONTENT,
      isAllowedMatch: isAllowedUncPath,
    },
    {
      id: 'sql-instance',
      pattern: SQL_INSTANCE_PATTERN,
      message: 'SQL Server named instance on a real host (use localhost or (localdb) in the starter)',
      scope: SCOPE_CONTENT,
      isAllowedMatch: isLocalSqlInstance,
    },
    {
      id: 'email',
      pattern: EMAIL_PATTERN,
      message: 'e-mail address of a person or organisation (use name@example.test)',
      scope: SCOPE_CONTENT,
      isAllowedMatch: isAllowedEmail,
    },
    {
      id: 'saudi-mobile',
      pattern: SAUDI_MOBILE_PATTERN,
      message: 'Saudi mobile number (use a placeholder such as 05XXXXXXXX)',
      scope: SCOPE_CONTENT,
      mask: maskNationalId,
    },
    {
      id: 'national-id',
      pattern: NATIONAL_ID_PATTERN,
      message: 'national-ID-like number (10 digits starting 1 or 2)',
      scope: SCOPE_CONTENT,
      mask: maskNationalId,
    },
    {
      id: 'internal-host',
      pattern: INTERNAL_HOST_PATTERN,
      message: 'internal or government host name (use example.test / localhost in the starter)',
      scope: SCOPE_CONTENT,
      isAllowedMatch: (host) => ALLOWED_HOSTS.has(host.toLowerCase()),
    },
    {
      id: 'ldap-domain',
      pattern: LDAP_DOMAIN_PATTERN,
      message: 'LDAP domain component of a real directory (use DC=example,DC=com)',
      scope: SCOPE_CONTENT,
    },
    {
      id: 'dga-package',
      pattern: DGA_PACKAGE_PATTERN,
      message: 'external DGA npm package reference (D5: the starter owns its DGA components in @codelab/ng-dga)',
      scope: SCOPE_CONTENT,
      appliesTo: isDgaCheckedFile,
    },
    {
      id: 'dga-tarball',
      pattern: DGA_TARBALL_PATTERN,
      message: 'external DGA package file (D5)',
      scope: SCOPE_PATH,
    },
  ];
}
