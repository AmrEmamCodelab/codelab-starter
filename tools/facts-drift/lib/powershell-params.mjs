// Reads the parameter names of a PowerShell script's top-level param() block, without running PowerShell.

const BLOCK_COMMENT = /<#[\s\S]*?#>/g;
const LINE_COMMENT = /(^|[^`$\w])#.*$/gm;
const SCRIPT_PARAM = /(?:^|[\s;}\]])param\s*\(/i;
const FUNCTION_KEYWORD = /(?:^|\s)function\s+[\w-]+/i;
const PARAMETER_NAME = /\$([A-Za-z_][A-Za-z0-9_]*)/g;
const DECLARATION_PRECEDERS = new Set(['', ',', '(']);
const SINGLE_QUOTE = "'";
const DOUBLE_QUOTE = '"';
const OPEN_PAREN = '(';
const CLOSE_PAREN = ')';
const OPEN_BRACKET = '[';
const CLOSE_BRACKET = ']';
const BLANK = ' ';

function stripComments(text) {
  return text.replace(BLOCK_COMMENT, (comment) => comment.replace(/[^\n]/g, BLANK)).replace(LINE_COMMENT, '$1');
}

/**
 * The text of a param block at its own nesting level: strings, [attributes]/[types] and nested (...) are blanked,
 * so what is left is "$Name, $Other = <default>" shapes.
 */
function topLevelText(text, openIndex) {
  let parenDepth = 0;
  let bracketDepth = 0;
  let quote = null;
  let output = '';
  for (let index = openIndex; index < text.length; index += 1) {
    const char = text[index];
    if (quote !== null) {
      if (char === quote) quote = null;
      output += BLANK;
      continue;
    }
    if (char === SINGLE_QUOTE || char === DOUBLE_QUOTE) {
      quote = char;
      output += BLANK;
    } else if (char === OPEN_BRACKET) {
      bracketDepth += 1;
      output += BLANK;
    } else if (char === CLOSE_BRACKET) {
      bracketDepth -= 1;
      output += BLANK;
    } else if (char === OPEN_PAREN) {
      parenDepth += 1;
      output += parenDepth === 1 && bracketDepth === 0 ? OPEN_PAREN : BLANK;
    } else if (char === CLOSE_PAREN) {
      parenDepth -= 1;
      if (parenDepth === 0) return output;
      output += BLANK;
    } else {
      output += parenDepth === 1 && bracketDepth === 0 ? char : BLANK;
    }
  }
  return output;
}

/**
 * Parameter names declared by the script's top-level param() block (lower-cased), or null when the script has none.
 * A param() that only appears after the first "function" belongs to that function, not to the script.
 * @param {string} scriptText
 * @returns {Set<string> | null}
 */
export function readScriptParameters(scriptText) {
  const text = stripComments(scriptText);
  const paramMatch = SCRIPT_PARAM.exec(text);
  if (paramMatch === null) return null;
  const functionMatch = FUNCTION_KEYWORD.exec(text);
  if (functionMatch !== null && functionMatch.index < paramMatch.index) return null;
  const openIndex = paramMatch.index + paramMatch[0].length - 1;
  const block = topLevelText(text, openIndex);
  const names = new Set();
  for (const match of block.matchAll(PARAMETER_NAME)) {
    const preceding = block.slice(0, match.index).trimEnd();
    if (DECLARATION_PRECEDERS.has(preceding.slice(-1))) names.add(match[1].toLowerCase());
  }
  return names;
}

/**
 * Resolves a switch the way PowerShell binds it: exact name, else a unique prefix of one parameter.
 * @param {string} name switch without the leading "-"
 * @param {Set<string>} parameters lower-cased names (script parameters plus common parameters)
 */
export function switchResolves(name, parameters) {
  const lower = name.toLowerCase();
  if (parameters.has(lower)) return true;
  return [...parameters].filter((parameter) => parameter.startsWith(lower)).length === 1;
}
