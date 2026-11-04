// Finding shape, text positions and console reporting shared by the scrub sweep and the secret scan.

/**
 * @typedef {{ path: string, line: number, column: number, ruleId: string, message: string, excerpt: string }} Finding
 * line PATH_FINDING_LINE (0) means the finding is in the file PATH, not in its content.
 */

export const PATH_FINDING_LINE = 0;
const MAX_EXCERPT_LENGTH = 120;
const ELLIPSIS = '...';

/** Creates a finding; the excerpt is cut so a minified line never floods the log. */
export function createFinding({ path, line, column, ruleId, message, excerpt }) {
  const cut = excerpt.length > MAX_EXCERPT_LENGTH ? `${excerpt.slice(0, MAX_EXCERPT_LENGTH)}${ELLIPSIS}` : excerpt;
  return { path, line, column, ruleId, message, excerpt: cut };
}

export const UNSCANNABLE_RULE_ID = 'unscannable-binary';
const UNSCANNABLE_COLUMN = 1;

/** A finding for a file (or container entry) whose content cannot be scanned: it must not pass silently. */
export function unscannableFinding({ path, reason }) {
  return createFinding({
    path, line: PATH_FINDING_LINE, column: UNSCANNABLE_COLUMN, ruleId: UNSCANNABLE_RULE_ID,
    message: `${reason}; convert it to text, or (images and fonts only) use an allowlisted extension`, excerpt: path,
  });
}

/**
 * Prints findings as "path:line:col  rule  message  [excerpt]" and a one-line summary.
 * @param {string} toolName shown in the summary
 * @param {Finding[]} findings
 * @param {number} fileCount number of files scanned
 * @param {(line: string) => void} [write]
 */
export function reportFindings(toolName, findings, fileCount, write = console.log) {
  for (const finding of findings) {
    const location = finding.line === PATH_FINDING_LINE
      ? `${finding.path} (file name)`
      : `${finding.path}:${finding.line}:${finding.column}`;
    write(`${location}  ${finding.ruleId}  ${finding.message}  [${finding.excerpt}]`);
  }
  const verdict = findings.length === 0 ? 'clean' : `${findings.length} finding(s)`;
  write(`${toolName}: ${fileCount} file(s) scanned, ${verdict}.`);
}

/**
 * Splits text into lines with their start offsets, so rules can report 1-based line/column cheaply.
 * @param {string} text
 * @returns {{ text: string, number: number }[]} lines without their line terminators
 */
export function splitLines(text) {
  return text.split('\n').map((line, index) => ({ text: line.replace(/\r$/, ''), number: index + 1 }));
}
