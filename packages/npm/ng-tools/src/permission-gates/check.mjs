// Runs the template permission gate over a folder: every `*.html` template and the inline templates of every `*.ts`
// file (specs, test helpers and declaration files excluded). Point it at the app's `features` folder.
import fs from 'node:fs';
import path from 'node:path';
import { GateFindingKinds, PERMISSION_GATE_FIX, findUngatedButtons } from './gate-rule.mjs';
import { extractInlineTemplates } from './inline-templates.mjs';

export const TEMPLATE_EXTENSION = '.html';
export const SCRIPT_EXTENSION = '.ts';
/** Script files that are not app code: their templates are test hosts. */
export const EXCLUDED_SCRIPT_SUFFIXES = Object.freeze(['.spec.ts', '.test.ts', '.testing.ts', '.d.ts']);
const SKIPPED_FOLDERS = new Set(['node_modules', 'dist', 'coverage', '.angular']);
const LINE_OFFSET = 1;

const isScript = (name) => name.endsWith(SCRIPT_EXTENSION) && !EXCLUDED_SCRIPT_SUFFIXES.some((suffix) => name.endsWith(suffix));

/** The checked files under `dir`, sorted, as absolute paths. */
export function listGateSources(dir) {
  const files = [];
  const visit = (folder) => {
    for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
      const entryPath = path.join(folder, entry.name);
      if (entry.isDirectory()) {
        if (!SKIPPED_FOLDERS.has(entry.name)) visit(entryPath);
      } else if (entry.isFile() && (entry.name.endsWith(TEMPLATE_EXTENSION) || isScript(entry.name))) {
        files.push(entryPath);
      }
    }
  };
  visit(dir);
  return files.sort();
}

/** Ungated buttons in one file's text: an `.html` template, or a `.ts` file's inline templates (source lines). */
export function findUngatedButtonsInSource(fileName, text) {
  if (fileName.endsWith(TEMPLATE_EXTENSION)) return findUngatedButtons(text);
  return extractInlineTemplates(text).flatMap((template) =>
    findUngatedButtons(template.text).map((finding) => ({ ...finding, line: finding.line + template.line - LINE_OFFSET })));
}

/**
 * Checks every template under `dir`.
 * @returns {{ findings: { kind: string, file: string, line: number, column: number, element: string }[], fileCount: number, error?: string }}
 *   `file` is relative to `dir`, with forward slashes
 */
export function checkPermissionGates(dir) {
  if (!fs.existsSync(dir) || !fs.statSync(dir).isDirectory()) {
    return { findings: [], fileCount: 0, error: `folder not found: ${dir}` };
  }
  const files = listGateSources(dir);
  const findings = files.flatMap((file) => {
    const relative = path.relative(dir, file).split(path.sep).join('/');
    return findUngatedButtonsInSource(file, fs.readFileSync(file, 'utf8')).map((finding) => ({ file: relative, ...finding }));
  });
  return { findings, fileCount: files.length };
}

const FINDING_TEXT = Object.freeze({
  [GateFindingKinds.ungatedButton]: 'has neither *hasPermission (on itself or an ancestor) nor data-public-action',
  [GateFindingKinds.literalKey]: 'gates on a string literal; use a typed key (PermissionKeys.<Key>)',
});

/** `file:line:column <element> ...` lines plus the fix. */
export function formatGateFindings(findings, fix = PERMISSION_GATE_FIX) {
  const lines = findings.map(({ kind, file, line, column, element }) => `  ${file}:${line}:${column} <${element}> ${FINDING_TEXT[kind]}`);
  return `Template permission gate (base.fe-permission-gated) failed for ${findings.length} finding(s):\n${lines.join('\n')}\nFix: ${fix}`;
}
