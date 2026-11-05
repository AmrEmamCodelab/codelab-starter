// Text edits of the two files that list modules: the solution (.slnx) and the host project's ProjectReference list.
// Pure functions over text. They keep the file's line endings and indentation, never reorder existing lines, and
// are idempotent: adding what is already there returns the text unchanged.
import { BACKEND_SOLUTION_FOLDER, CRLF, MODULES_ITEM_GROUP_LABEL, NEWLINE } from './constants.mjs';

const INDENT_UNIT = '  ';
const BACKSLASH = /\\/g;
const SOLUTION_END = /^([ \t]*)<\/Solution>/m;
const PROJECT_END = /^([ \t]*)<\/Project>\s*$/m;
const REGEX_SPECIAL = /[.*+?^${}()|[\]\\]/g;

function newlineOf(text) {
  return text.includes(CRLF) ? CRLF : NEWLINE;
}

function escapeRegex(value) {
  return value.replace(REGEX_SPECIAL, '\\$&');
}

function samePath(left, right) {
  return left.replace(BACKSLASH, '/').toLowerCase() === right.replace(BACKSLASH, '/').toLowerCase();
}

function listedValues(text, pattern) {
  return [...text.matchAll(pattern)].map((match) => match[1]);
}

/**
 * Adds `<Project Path="..." />` entries to the solution folder (default /backend/), creating the folder before
 * `</Solution>` when it is missing. Paths already listed anywhere in the solution are skipped.
 * @param {string} text the .slnx content
 * @param {string[]} projectPaths repo-relative paths with '/' separators
 * @returns {{ text: string, added: string[] }}
 */
export function addSolutionProjects(text, projectPaths, folderName = BACKEND_SOLUTION_FOLDER) {
  const listed = listedValues(text, /<Project\s+Path="([^"]+)"/g);
  const missing = projectPaths.filter((candidate) => !listed.some((existing) => samePath(existing, candidate)));
  if (missing.length === 0) return { text, added: [] };
  const eol = newlineOf(text);
  const folder = new RegExp(`^([ \\t]*)<Folder\\s+Name="${escapeRegex(folderName)}"\\s*>[\\s\\S]*?^([ \\t]*)<\\/Folder>`, 'm');
  const match = folder.exec(text);
  if (match) {
    const indent = match[1] + INDENT_UNIT;
    const lines = missing.map((projectPath) => `${indent}<Project Path="${projectPath}" />${eol}`).join('');
    const closeAt = match.index + match[0].length - (match[2].length + '</Folder>'.length);
    return { text: text.slice(0, closeAt) + lines + text.slice(closeAt), added: missing };
  }
  const end = SOLUTION_END.exec(text);
  if (!end) throw new Error('The solution file has no </Solution> element.');
  const indent = end[1] + INDENT_UNIT;
  const block = [
    `${indent}<Folder Name="${folderName}">`,
    ...missing.map((projectPath) => `${indent}${INDENT_UNIT}<Project Path="${projectPath}" />`),
    `${indent}</Folder>`,
  ].join(eol) + eol;
  return { text: text.slice(0, end.index) + block + text.slice(end.index), added: missing };
}

/**
 * Adds `<ProjectReference Include="..." />` to the ItemGroup labelled Modules, creating that ItemGroup before
 * `</Project>` when it is missing. An Include already referenced anywhere in the project is skipped.
 * @param {string} text the .csproj content
 * @param {string} include the Include value as MSBuild writes it (backslash separators)
 * @returns {{ text: string, added: boolean }}
 */
export function addModuleProjectReference(text, include) {
  const listed = listedValues(text, /<ProjectReference\s+Include="([^"]+)"/g);
  if (listed.some((existing) => samePath(existing, include))) return { text, added: false };
  const eol = newlineOf(text);
  const group = new RegExp(`^([ \\t]*)<ItemGroup\\s+Label="${MODULES_ITEM_GROUP_LABEL}"\\s*>[\\s\\S]*?^([ \\t]*)<\\/ItemGroup>`, 'm');
  const match = group.exec(text);
  if (match) {
    const line = `${match[1]}${INDENT_UNIT}<ProjectReference Include="${include}" />${eol}`;
    const closeAt = match.index + match[0].length - (match[2].length + '</ItemGroup>'.length);
    return { text: text.slice(0, closeAt) + line + text.slice(closeAt), added: true };
  }
  const end = PROJECT_END.exec(text);
  if (!end) throw new Error('The project file has no </Project> element.');
  const indent = end[1] + INDENT_UNIT;
  const block = [
    `${indent}<ItemGroup Label="${MODULES_ITEM_GROUP_LABEL}">`,
    `${indent}${INDENT_UNIT}<ProjectReference Include="${include}" />`,
    `${indent}</ItemGroup>`,
    '',
  ].join(eol) + eol;
  return { text: text.slice(0, end.index) + block + text.slice(end.index), added: true };
}

/** The module names the host project references (Starter.Modules.<Name>.csproj). */
export function referencedModules(hostProjectText, solutionToken) {
  const pattern = new RegExp(`${escapeRegex(solutionToken)}\\.Modules\\.([A-Za-z0-9]+)\\.csproj$`);
  return listedValues(hostProjectText, /<ProjectReference\s+Include="([^"]+)"/g).flatMap((include) => {
    const match = pattern.exec(include);
    return match ? [match[1]] : [];
  });
}
