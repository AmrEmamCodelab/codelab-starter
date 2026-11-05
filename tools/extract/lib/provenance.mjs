// Harvest manifest check (harvest copy vs the source commit's tree) and the provenance/SOURCE.md renderer.
// Pure functions: git and file access live in generate-source.mjs.

const LS_TREE_LINE = /^(\d{6}) (\w+) ([0-9a-f]{40})\t(.+)$/s;
const BLOB_TYPE = 'blob';
// Lists longer than this are cut in SOURCE.md (the full list is printed by the generator).
export const MAX_LISTED_PATHS = 50;
const DIRECTORY_GROUP_DEPTH = 2;

/**
 * Parses `git ls-tree -r -z` output.
 * @param {string} output NUL-separated records "<mode> <type> <sha>\t<path>"
 * @returns {{ mode: string, type: string, sha: string, path: string }[]}
 */
export function parseLsTree(output) {
  return output.split('\0').filter(Boolean).map((record) => {
    const match = LS_TREE_LINE.exec(record);
    if (!match) throw new Error(`unexpected ls-tree record: ${record}`);
    const [, mode, type, sha, path] = match;
    return { mode, type, sha, path };
  });
}

/**
 * Compares the source tree with the harvest copy.
 * @param {{ type: string, sha: string, path: string }[]} treeEntries from parseLsTree
 * @param {Map<string, string>} harvestHashes harvest path -> git blob id (hash-object --no-filters)
 * @returns {{ total: number, matched: number, mismatched: {path: string, expected: string, actual: string}[],
 *             missing: string[], extra: string[], nonBlob: string[] }}
 */
export function compareHarvest(treeEntries, harvestHashes) {
  const blobs = treeEntries.filter((entry) => entry.type === BLOB_TYPE);
  const nonBlob = treeEntries.filter((entry) => entry.type !== BLOB_TYPE).map((entry) => entry.path);
  const treePaths = new Set(blobs.map((entry) => entry.path));
  const mismatched = [];
  const missing = [];
  let matched = 0;
  for (const entry of blobs) {
    const actual = harvestHashes.get(entry.path);
    if (actual === undefined) missing.push(entry.path);
    else if (actual === entry.sha) matched += 1;
    else mismatched.push({ path: entry.path, expected: entry.sha, actual });
  }
  const extra = [...harvestHashes.keys()].filter((path) => !treePaths.has(path)).sort();
  return { total: blobs.length, matched, mismatched, missing, extra, nonBlob };
}

/**
 * Marks each mismatch whose harvest bytes, with CRLF turned back into LF, hash to the expected blob: the copy differs
 * only by line endings (git archive applies text/eol attributes on export).
 * @param {{path: string, expected: string}[]} mismatched
 * @param {(path: string) => string} lfNormalizedBlobId git blob id of the harvest file after CRLF -> LF
 */
export function classifyLineEndingMismatches(mismatched, lfNormalizedBlobId) {
  return mismatched.map((item) => ({ ...item, lineEndingsOnly: lfNormalizedBlobId(item.path) === item.expected }));
}

function directoryOf(path) {
  return path.split('/').slice(0, DIRECTORY_GROUP_DEPTH).join('/');
}

/** Counts paths per leading directory, for a readable summary of a large list. */
export function groupByDirectory(paths) {
  const counts = new Map();
  for (const path of paths) counts.set(directoryOf(path), (counts.get(directoryOf(path)) ?? 0) + 1);
  return [...counts.entries()].sort(([left], [right]) => left.localeCompare(right));
}

/** True when every tree blob is present with the same hash and the harvest holds nothing else. */
export function isFullMatch(comparison) {
  return comparison.matched === comparison.total
    && comparison.mismatched.length === 0
    && comparison.missing.length === 0
    && comparison.extra.length === 0
    && comparison.nonBlob.length === 0;
}

function listSection(title, items) {
  if (items.length === 0) return [];
  const shown = items.slice(0, MAX_LISTED_PATHS).map((item) => `- \`${item}\``);
  const more = items.length > MAX_LISTED_PATHS ? [`- ... and ${items.length - MAX_LISTED_PATHS} more`] : [];
  return ['', `**${title}** (${items.length}):`, '', ...shown, ...more];
}

function lineEndingSection(lineEndingOnly) {
  if (lineEndingOnly.length === 0) return [];
  return [
    '',
    `**Line endings only** (${lineEndingOnly.length}): with CRLF turned back into LF these files hash to the exact`,
    'blob in the tree, so their CONTENT matches. Cause: a nested `.gitattributes` with `text=auto` makes',
    '`git archive` write CRLF on Windows even with `core.autocrlf=false`, because `core.eol` defaults to native.',
    'Remedy (owner, outside this repo): re-create the harvest copy with',
    '`git -C <source> -c core.autocrlf=false -c core.eol=lf archive <commit>` and rerun this generator.',
    '',
    '| Folder | Files |',
    '|---|---|',
    ...groupByDirectory(lineEndingOnly.map((item) => item.path)).map(([folder, count]) => `| \`${folder}\` | ${count} |`),
  ];
}

function manifestCheckSection(comparison) {
  const lineEndingOnly = comparison.mismatched.filter((item) => item.lineEndingsOnly);
  const realMismatches = comparison.mismatched.filter((item) => !item.lineEndingsOnly);
  const verdict = isFullMatch(comparison)
    ? `**PASS: 100% match.** All ${comparison.total} files of the source tree are present in the harvest copy with `
      + 'identical git blob ids, and the harvest copy holds no other file.'
    : `**FAIL (byte-exact).** ${comparison.matched} of ${comparison.total} files match byte for byte; `
      + `${lineEndingOnly.length} differ only in line endings; ${realMismatches.length} differ in content; `
      + `${comparison.missing.length} missing; ${comparison.extra.length} extra.`;
  return [
    '## Harvest manifest check',
    '',
    'Method (read-only): `git ls-tree -r <commit>` in the source repository, compared with',
    '`git hash-object --no-filters` of every file in the harvest copy (nothing is written to either).',
    '',
    verdict,
    ...lineEndingSection(lineEndingOnly),
    ...listSection('Content mismatches', realMismatches.map((item) => `${item.path} expected ${item.expected} got ${item.actual}`)),
    ...listSection('Missing from the harvest copy', comparison.missing),
    ...listSection('In the harvest copy but not in the tree', comparison.extra),
    ...listSection('Non-blob tree entries (submodules)', comparison.nonBlob),
  ];
}

/** The starter-path cell of an entry left in staging: it has no repository target. */
const STAGED_TARGET = '(left in staging)';

function originSection(origins) {
  const header = ['## Per-file origin map', ''];
  if (origins.length === 0) {
    return [...header, 'No harvested file has been promoted into the repository yet. Each promoted batch adds',
      '`provenance/origins/<batch>.json` (written by `tools/extract/extract.ps1`); rerun the generator to list it here.'];
  }
  const rows = [...origins]
    .sort((left, right) => (left.target ?? left.source).localeCompare(right.target ?? right.source))
    .map((origin) => `| ${origin.target === undefined ? STAGED_TARGET : `\`${origin.target}\``} | \`${origin.source}\` | ${origin.batch} / ${origin.entryId} | ${origin.mode} `
      + `| ${origin.promotion ?? DEFAULT_PROMOTION} | \`${origin.sourceGitBlob}\` |`);
  return [
    ...header,
    'Status `rewritten`: the repo file at the starter path was rewritten from the source file. Status `staged`: the batch',
    'was extracted and scanned, and the starter path is its staging path; a later phase rewrites and promotes it.',
    '',
    '| Starter path | Source path | Batch / entry | Mode | Status | Source blob |',
    '|---|---|---|---|---|---|',
    ...rows,
  ];
}

const DEFAULT_PROMOTION = 'rewritten';

/**
 * Renders provenance/SOURCE.md.
 * @param {{ source: object, resolvedCommit: string, resolvedTree: string, generatedOn: string, comparison: object,
 *           origins: object[], hardDeny: { caseInsensitive: string[], caseSensitive: string[] } }} model
 */
export function renderSourceMarkdown({ source, resolvedCommit, resolvedTree, generatedOn, comparison, origins, hardDeny }) {
  const treeVerdict = resolvedTree === source.treeHash ? 'matches the manifest' : `DIFFERS from the manifest (${source.treeHash})`;
  const denied = [...hardDeny.caseInsensitive, ...hardDeny.caseSensitive.map((pattern) => `${pattern} (case-sensitive)`)];
  return [
    '# Provenance: where the starter came from',
    '',
    'Generated by `node tools/extract/generate-source.mjs`. Do not edit by hand.',
    '',
    '## Source',
    '',
    '| Item | Value |',
    '|---|---|',
    `| Product | ${source.product} (fixed commit, never modified; D6) |`,
    `| Repository | \`${source.repository}\` (read-only git only) |`,
    `| Branch | \`${source.branch ?? ''}\` |`,
    `| Commit | \`${resolvedCommit}\` |`,
    `| Tree hash | \`${resolvedTree}\` (${treeVerdict}) |`,
    `| Harvest copy | \`${source.harvestRoot}\` (git archive of the commit, read-only) |`,
    `| Generated on | ${generatedOn} |`,
    '',
    ...manifestCheckSection(comparison),
    '',
    ...originSection(origins),
    '',
    '## Never harvested (hard-deny)',
    '',
    'From `tools/extract/allowlist.manifest.yaml`; these win over any allowlist entry:',
    '',
    ...denied.map((pattern) => `- \`${pattern}\``),
    '',
  ].join('\n');
}
