// Copies a resolved batch from the harvest into a staging folder and records one origin entry per file.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { gitBlobId, sha256Hex } from './hashing.mjs';
import { MODE_HEADINGS_ONLY } from './manifest.mjs';

const MARKDOWN_HEADING = /^#{1,6}\s/;
const CODE_FENCE = /^\s*(?:```|~~~)/;
const STRUCTURE_ONLY_NOTE = '<!-- Structure only: headings harvested by tools/extract; every body is rewritten for the starter. -->';

/**
 * Keeps only the Markdown headings (outside code fences), so a document's STRUCTURE can be reused without its text.
 * @param {string} text Markdown
 */
export function headingsOnly(text) {
  const headings = [];
  let inFence = false;
  for (const line of text.split(/\r?\n/)) {
    if (CODE_FENCE.test(line)) inFence = !inFence;
    else if (!inFence && MARKDOWN_HEADING.test(line)) headings.push(line.trimEnd());
  }
  return `${STRUCTURE_ONLY_NOTE}\n\n${headings.join('\n\n')}\n`;
}

function stagedBytes(copy, sourceBytes) {
  if (copy.mode === MODE_HEADINGS_ONLY) return Buffer.from(headingsOnly(sourceBytes.toString('utf8')), 'utf8');
  return sourceBytes;
}

/**
 * Writes every copy under stagingFiles and returns its origin entries.
 * @param {{ batchName: string, harvestRoot: string, stagingFiles: string,
 *           copies: {entryId: string, source: string, target: string, mode: string}[] }} request
 * @returns {object[]} origin entries: batch, entryId, source, target, mode, sourceGitBlob, sourceSha256, stagedSha256
 */
export function stageCopies({ batchName, harvestRoot, stagingFiles, copies }) {
  return copies.map((copy) => {
    const sourceBytes = readFileSync(join(harvestRoot, copy.source));
    const bytes = stagedBytes(copy, sourceBytes);
    const destination = join(stagingFiles, copy.target);
    mkdirSync(dirname(destination), { recursive: true });
    writeFileSync(destination, bytes);
    return {
      batch: batchName,
      entryId: copy.entryId,
      source: copy.source,
      target: copy.target,
      mode: copy.mode,
      sourceGitBlob: gitBlobId(sourceBytes),
      sourceSha256: sha256Hex(sourceBytes),
      stagedSha256: sha256Hex(bytes),
    };
  });
}
