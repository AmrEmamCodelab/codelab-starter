// Helpers for Angular attribute values: whether an attribute is a property binding, and the string literals of a
// binding expression.

const BOUND = /^\[.*\]$/;
const QUOTED_LITERAL = /'([^']*)'|"([^"]*)"/g;
const SINGLE_QUOTED_GROUP = 1;
const DOUBLE_QUOTED_GROUP = 2;

/** True when the attribute is a property binding (`[name]`), so its value is an expression. */
export function isBound(attributeName) {
  return BOUND.test(attributeName);
}

/** The quoted string literals of an expression: `on ? 'a' : "b"` gives ['a', 'b']. */
export function quotedLiterals(expression) {
  return [...expression.matchAll(QUOTED_LITERAL)].map((match) => match[SINGLE_QUOTED_GROUP] ?? match[DOUBLE_QUOTED_GROUP]);
}

/** The literal values an attribute gives: its trimmed text when static, the quoted literals when bound. */
export function literalValuesOf(attributeName, value) {
  if (value === null) return [];
  return isBound(attributeName) ? quotedLiterals(value) : [value.trim()];
}
