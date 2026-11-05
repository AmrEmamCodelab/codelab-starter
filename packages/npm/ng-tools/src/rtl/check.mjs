// Runs the RTL lint over a folder: every `.html` template, and every `.ts` file (its inline templates with the template
// rules, the rest of its code with the TypeScript rules). Specs, test helpers and declaration files are skipped.
import fs from 'node:fs';
import { extractInlineTemplates } from '../permission-gates/inline-templates.mjs';
import { blankRanges, lexScript } from '../shared/script-lexer.mjs';
import { TEMPLATE_EXTENSION, isFolder, listSourceFiles, matchesAnyGlob, positionFinder, toPosixRelative } from '../shared/source-files.mjs';
import { RTL_LINT_FIX, RtlFindingKinds, findRtlScriptFindings, findRtlTemplateFindings, findUnmirroredIcons, mirroredIconsOf } from './rtl-rules.mjs';

/** Files that may fix the document direction by default: the host page sets <html dir> before the app starts. */
export const DEFAULT_DIRECTION_FILES = Object.freeze(['index.html']);

const LINE_OFFSET = 1;

/**
 * RTL findings in one file's text, with source lines. `fileName` decides the language (`.html` or TypeScript).
 * @param {string} fileName
 * @param {string} text
 * @param {{ mirroredIcons?: Set<string> | null, allowDirection?: boolean }} [options]
 * @returns {{ kind: string, line: number, column: number, detail: string, element?: string }[]}
 */
export function findRtlFindingsInSource(fileName, text, options = {}) {
  if (fileName.endsWith(TEMPLATE_EXTENSION)) return findRtlTemplateFindings(text, options);
  const templates = extractInlineTemplates(text);
  const templateLines = new Set(templates.map((template) => template.line));
  const { code, strings } = lexScript(text);
  const positionOf = positionFinder(text);
  const isTemplateString = (literal) => templateLines.has(positionOf(literal.offset).line);
  const scriptFindings = findRtlScriptFindings(
    { code: blankRanges(code, strings.filter(isTemplateString)), strings: strings.filter((literal) => !isTemplateString(literal)) },
    options,
  ).map(({ kind, offset, detail }) => ({ kind, ...positionOf(offset), detail }));
  const templateFindings = templates.flatMap((template) => findRtlTemplateFindings(template.text, options)
    .map((finding) => ({ ...finding, line: finding.line + template.line - LINE_OFFSET })));
  return [...templateFindings, ...scriptFindings].sort((first, second) => first.line - second.line || first.column - second.column);
}

/**
 * Checks every template and TypeScript file under `dir`.
 * @param {string} dir
 * @param {{ iconMap?: object | null, iconMapFile?: string, directionFiles?: string[] }} [options] `iconMap`: the
 *   parsed icon code point map (its mirrored icons drive the icon rule, and its directional icons must mirror);
 *   `iconMapFile`: the name reported for map findings; `directionFiles`: globs (relative to `dir`) of files that may
 *   fix the direction
 * @returns {{ findings: { kind: string, file: string, line: number, column: number, detail: string }[], fileCount: number, error?: string }}
 */
export function checkRtl(dir, { iconMap = null, iconMapFile = 'icon map', directionFiles = DEFAULT_DIRECTION_FILES } = {}) {
  if (!isFolder(dir)) return { findings: [], fileCount: 0, error: `folder not found: ${dir}` };
  const files = listSourceFiles(dir);
  const mirroredIcons = iconMap === null ? null : mirroredIconsOf(iconMap);
  const findings = files.flatMap((file) => {
    const relative = toPosixRelative(dir, file);
    const options = { mirroredIcons, allowDirection: matchesAnyGlob(relative, directionFiles) };
    return findRtlFindingsInSource(file, fs.readFileSync(file, 'utf8'), options).map((finding) => ({ file: relative, ...finding }));
  });
  const mapFindings = iconMap === null ? [] : findUnmirroredIcons(iconMap).map((finding) => ({ file: iconMapFile, line: LINE_OFFSET, column: LINE_OFFSET, ...finding }));
  return { findings: [...findings, ...mapFindings], fileCount: files.length };
}

const FINDING_TEXT = Object.freeze({
  [RtlFindingKinds.physicalStyleBinding]: 'binds a physical-side style',
  [RtlFindingKinds.physicalStyle]: 'names a physical side',
  [RtlFindingKinds.physicalAlignment]: 'aligns or floats to a physical side',
  [RtlFindingKinds.hardCodedDirection]: 'hard-codes the text direction',
  [RtlFindingKinds.physicalIconName]: 'uses a physically named icon',
  [RtlFindingKinds.iconMirrorDisabled]: 'switches off RTL mirroring of a directional icon',
  [RtlFindingKinds.iconNotMirrored]: 'is a directional icon without mirrorInRtl',
});

/** `file:line:column <kind> detail` lines plus the fix. */
export function formatRtlFindings(findings, fix = RTL_LINT_FIX) {
  const lines = findings.map(({ kind, file, line, column, detail }) => `  ${file}:${line}:${column} ${FINDING_TEXT[kind]} (${kind}): ${detail}`);
  return `RTL lint (base.fe-tokens-logical-css) failed for ${findings.length} finding(s):\n${lines.join('\n')}\nFix: ${fix}`;
}
