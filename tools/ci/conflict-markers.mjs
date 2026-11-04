#!/usr/bin/env node
// CI step 8: fail when any text file under --root contains a git merge-conflict marker.
//
// Usage: node tools/ci/conflict-markers.mjs --root <path>
// Exit codes: 0 clean, 1 markers found, 2 usage or tool error.
//
// Rules (shared with the docs gate's DOCS004, tools/docs-gate/src/Checks/MergeMarkerScanner.cs):
// - a start, base or end marker (seven '<', '|' or '>' at the start of a line, alone or followed by a space) is reported;
// - the separator (exactly seven '=') is reported only inside an open conflict, because on its own it is a valid
//   Markdown heading underline.
// Markers are built from MARKER_LENGTH below, so this file never contains one.

import { spawnSync } from 'node:child_process';
import { closeSync, openSync, readdirSync, readFileSync, readSync, statSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const EXIT_CLEAN = 0;
export const EXIT_FINDINGS = 1;
export const EXIT_USAGE = 2;

const MARKER_LENGTH = 7;
const LABEL_SEPARATOR = ' ';
const START_MARKER = '<'.repeat(MARKER_LENGTH);
const BASE_MARKER = '|'.repeat(MARKER_LENGTH);
const SEPARATOR_MARKER = '='.repeat(MARKER_LENGTH);
const END_MARKER = '>'.repeat(MARKER_LENGTH);

const ROOT_OPTION = '--root';
const DEFAULT_ROOT = '.';
const GIT_SUCCESS = 0;
const NUL = '\0';
// Same probe size git uses to decide a file is binary: a NUL byte in the first 8000 bytes.
const BINARY_PROBE_BYTES = 8000;
const NUL_BYTE = 0;
// Larger files are generated or vendored artefacts, never hand-merged text.
const MAX_TEXT_FILE_BYTES = 10 * 1024 * 1024;

/** Folders never scanned, even outside git (dependencies, build output, VCS data). Matches the docs gate. */
export const EXCLUDED_DIRECTORIES = new Set(['.git', 'node_modules', 'bin', 'obj', '.vs', '.angular', 'testresults']);

/**
 * Returns { line, marker } for every conflict marker in text (lines are 1-based).
 * @param {string} text
 */
export function scanText(text) {
  const findings = [];
  let insideConflict = false;
  text.split('\n').forEach((rawLine, index) => {
    const line = rawLine.endsWith('\r') ? rawLine.slice(0, -1) : rawLine;
    const marker = classifyLine(line, insideConflict);
    if (marker === null) return;
    if (marker === START_MARKER) insideConflict = true;
    if (marker === END_MARKER) insideConflict = false;
    findings.push({ line: index + 1, marker });
  });
  return findings;
}

function classifyLine(line, insideConflict) {
  for (const marker of [START_MARKER, BASE_MARKER, END_MARKER]) {
    if (isMarker(line, marker)) return marker;
  }
  return insideConflict && line === SEPARATOR_MARKER ? SEPARATOR_MARKER : null;
}

function isMarker(line, marker) {
  return line.startsWith(marker) && (line.length === marker.length || line[marker.length] === LABEL_SEPARATOR);
}

/**
 * Root-relative files ('/' separators) to scan: git's tracked + untracked-not-ignored list inside a work tree,
 * every file otherwise. Excluded folders are always skipped.
 * @param {string} root
 */
export function listFiles(root) {
  const fromGit = listFilesFromGit(root);
  const candidates = fromGit ?? listFilesFromDisk(root, '');
  return [...new Set(candidates)].filter((file) => !isInExcludedDirectory(file)).sort();
}

function listFilesFromGit(root) {
  const result = spawnSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], {
    cwd: root,
    encoding: 'utf8',
    maxBuffer: Number.MAX_SAFE_INTEGER,
  });
  if (result.error || result.status !== GIT_SUCCESS) return null;
  return result.stdout.split(NUL).filter((file) => file.length > 0);
}

function listFilesFromDisk(root, relativeDirectory) {
  const files = [];
  for (const entry of readdirSync(path.join(root, relativeDirectory), { withFileTypes: true })) {
    const relative = relativeDirectory ? `${relativeDirectory}/${entry.name}` : entry.name;
    if (entry.isDirectory() && !EXCLUDED_DIRECTORIES.has(entry.name.toLowerCase())) {
      files.push(...listFilesFromDisk(root, relative));
    } else if (entry.isFile()) {
      files.push(relative);
    }
  }
  return files;
}

function isInExcludedDirectory(file) {
  return file.split('/').slice(0, -1).some((segment) => EXCLUDED_DIRECTORIES.has(segment.toLowerCase()));
}

function readTextOrNull(fullPath) {
  let stats;
  try {
    stats = statSync(fullPath);
  } catch {
    return null; // listed by git but deleted from the working tree
  }
  if (!stats.isFile() || stats.size > MAX_TEXT_FILE_BYTES || looksBinary(fullPath)) return null;
  return readFileSync(fullPath, 'utf8');
}

function looksBinary(fullPath) {
  const probe = Buffer.alloc(BINARY_PROBE_BYTES);
  const descriptor = openSync(fullPath, 'r');
  try {
    const bytesRead = readSync(descriptor, probe, 0, BINARY_PROBE_BYTES, 0);
    return probe.subarray(0, bytesRead).includes(NUL_BYTE);
  } finally {
    closeSync(descriptor);
  }
}

/**
 * Scans every text file under root; returns { file, line, marker } findings.
 * @param {string} root
 */
export function findConflictMarkers(root) {
  const findings = [];
  for (const file of listFiles(root)) {
    const text = readTextOrNull(path.join(root, file));
    if (text === null) continue;
    for (const { line, marker } of scanText(text)) findings.push({ file, line, marker });
  }
  return findings;
}

/** Parses argv (after the script path); returns { root } or { error }. */
export function parseArguments(argv) {
  if (argv.length === 0) return { root: DEFAULT_ROOT };
  if (argv.length === 2 && argv[0] === ROOT_OPTION) return { root: argv[1] };
  return { error: `Usage: node tools/ci/conflict-markers.mjs ${ROOT_OPTION} <path>` };
}

/** Runs the check and returns the exit code. */
export function main(argv, log = console.log, logError = console.error) {
  const { root, error } = parseArguments(argv);
  if (error) {
    logError(error);
    return EXIT_USAGE;
  }
  const fullRoot = path.resolve(root);
  if (!statSync(fullRoot, { throwIfNoEntry: false })?.isDirectory()) {
    logError(`conflict markers: root '${fullRoot}' is not a folder.`);
    return EXIT_USAGE;
  }

  const findings = findConflictMarkers(fullRoot);
  for (const { file, line, marker } of findings) log(`${file}:${line}: merge-conflict marker '${marker}'`);
  if (findings.length === 0) {
    log('conflict markers: PASSED');
    return EXIT_CLEAN;
  }
  log(`conflict markers: FAILED with ${findings.length} marker line(s)`);
  return EXIT_FINDINGS;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = main(process.argv.slice(2));
}
