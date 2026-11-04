// Applies an Edit tool call (old_string -> new_string) to the current file text, in memory, so the hook can
// evaluate the WHOLE resulting file. Mirrors the Edit tool: exact match, first occurrence unless replace_all.
// String.prototype.replace is avoided on purpose: it interprets `$&`, `$1`... inside new_string.

const CRLF = /\r\n/g;
const LF = '\n';

function replaceFirst(text, oldString, newString) {
  const index = text.indexOf(oldString);
  if (index < 0) return null;
  return text.slice(0, index) + newString + text.slice(index + oldString.length);
}

function replaceEvery(text, oldString, newString) {
  if (!text.includes(oldString)) return null;
  return text.split(oldString).join(newString);
}

function applyOnce(text, { oldString, newString, replaceAll }) {
  return replaceAll ? replaceEvery(text, oldString, newString) : replaceFirst(text, oldString, newString);
}

/**
 * The file text after the edit, or null when old_string is not found (the Edit tool will then fail by itself).
 * Falls back to LF-normalized matching for CRLF files, because the model usually sends LF text.
 */
export function simulateEdit({ original, oldString, newString, replaceAll = false }) {
  if (!oldString) return null;
  const direct = applyOnce(original, { oldString, newString, replaceAll });
  if (direct !== null) return direct;
  return applyOnce(original.replace(CRLF, LF), {
    oldString: oldString.replace(CRLF, LF),
    newString,
    replaceAll,
  });
}
