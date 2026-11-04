/** Raised when one or more rules files are invalid. `problems` holds one human-readable line per problem. */
export class RulesError extends Error {
  /** @param {string[]} problems */
  constructor(problems) {
    super(`Rules are invalid (${problems.length} problem(s)):\n  - ${problems.join('\n  - ')}`);
    this.name = 'RulesError';
    this.problems = problems;
  }
}

/** Collects problems and throws them together, so one run reports every mistake. */
export class ProblemList {
  #problems = [];

  /** @param {string} file @param {string} message */
  add(file, message) {
    this.#problems.push(`${file}: ${message}`);
  }

  get count() {
    return this.#problems.length;
  }

  throwIfAny() {
    if (this.#problems.length > 0) {
      throw new RulesError(this.#problems);
    }
  }
}
