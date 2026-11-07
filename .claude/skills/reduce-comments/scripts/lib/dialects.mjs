// Maps a file to the scanner for its language. Files of any other type are not machine-verifiable.
import { extname } from 'node:path';
import { scanCSharp } from './scan-csharp.mjs';
import { scanHtml } from './scan-html.mjs';
import { scanScript } from './scan-script.mjs';
import { scanSql } from './scan-sql.mjs';

export const Dialect = Object.freeze({
  CSHARP: 'csharp',
  SCRIPT: 'script',
  HTML: 'html',
  SQL: 'sql',
});

const SCANNERS = Object.freeze({
  [Dialect.CSHARP]: scanCSharp,
  [Dialect.SCRIPT]: scanScript,
  [Dialect.HTML]: scanHtml,
  [Dialect.SQL]: scanSql,
});

// JSX/TSX are left out on purpose: `//` inside JSX text is text, not a comment, and this scanner cannot tell.
const EXTENSIONS = Object.freeze({
  '.cs': Dialect.CSHARP,
  '.csx': Dialect.CSHARP,
  '.ts': Dialect.SCRIPT,
  '.mts': Dialect.SCRIPT,
  '.cts': Dialect.SCRIPT,
  '.js': Dialect.SCRIPT,
  '.mjs': Dialect.SCRIPT,
  '.cjs': Dialect.SCRIPT,
  '.html': Dialect.HTML,
  '.htm': Dialect.HTML,
  '.sql': Dialect.SQL,
});

export const SUPPORTED_EXTENSIONS = Object.freeze(Object.keys(EXTENSIONS));

/** The dialect for a file path, or null when the extension is not supported. */
export function dialectForPath(path) {
  return EXTENSIONS[extname(path).toLowerCase()] ?? null;
}

export function isDialect(value) {
  return Object.values(Dialect).includes(value);
}

export function scan(text, dialect) {
  const scanner = SCANNERS[dialect];
  if (!scanner) {
    throw new Error(`Unknown dialect '${dialect}'.`);
  }
  return scanner(text);
}
