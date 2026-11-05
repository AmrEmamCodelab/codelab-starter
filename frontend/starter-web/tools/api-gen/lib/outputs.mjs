// Writes generated outputs, or (with --check) compares them to what is on disk. Line endings are normalised to LF
// on both sides, so a Windows checkout with autocrlf does not count as drift.

import fs from 'node:fs';
import path from 'node:path';

/** Text with every CRLF turned into LF. */
export function normaliseLineEndings(text) {
  return text.replace(/\r\n/g, '\n');
}

function readIfPresent(filePath) {
  return fs.existsSync(filePath) ? normaliseLineEndings(fs.readFileSync(filePath, 'utf8')) : null;
}

/**
 * Applies the outputs (`[{ path, content }]`, absolute paths). With `check: true` nothing is written.
 * Returns `{ drifted, written }`: the paths whose disk content differs (or is missing), and the paths written.
 */
export function syncOutputs(outputs, { check }) {
  const drifted = [];
  const written = [];
  for (const output of outputs) {
    const expected = normaliseLineEndings(output.content);
    if (readIfPresent(output.path) === expected) continue;
    drifted.push(output.path);
    if (check) continue;
    fs.mkdirSync(path.dirname(output.path), { recursive: true });
    fs.writeFileSync(output.path, expected);
    written.push(output.path);
  }
  return { drifted, written };
}
