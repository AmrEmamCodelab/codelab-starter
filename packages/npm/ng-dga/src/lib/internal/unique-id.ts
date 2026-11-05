let counter = 0;

/**
 * A document-unique id for linking a control to its label, hint and error (`for`, `aria-describedby`). The prefix
 * names the component, e.g. `dga-text-input-3`. Ids are stable for the life of a component instance.
 */
export function createDgaId(prefix: string): string {
  counter += 1;
  return `${prefix}-${counter}`;
}
