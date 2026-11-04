/**
 * The comparison every vector spec makes, with the C# runner's semantics: every `expected…` field
 * of a case must be produced, byte for byte, and a case that expects nothing fails as vacuous.
 */

import { expect } from 'vitest';
import { expectedValues, type VectorCase } from './vector-files.ts';

export function expectProduced(vectorCase: VectorCase, produced: Readonly<Record<string, string>>): void {
  const expected = expectedValues(vectorCase);
  expect(Object.keys(expected).length, `${vectorCase.name} expects nothing`).toBeGreaterThan(0);
  for (const [field, value] of Object.entries(expected)) {
    expect(produced[field], `${vectorCase.name}.${field}`).toBe(value);
  }
}

/** The cases of one kind, refusing an empty selection so a renamed kind cannot pass silently. */
export function casesOfKind(cases: readonly VectorCase[], kind: string): VectorCase[] {
  const selected = cases.filter((vectorCase) => vectorCase.kind === kind);
  if (selected.length === 0) throw new Error(`No '${kind}' cases in the vector file.`);
  return selected;
}
