// Validates provenance/origins/*.json: every harvested file is either promoted ("rewritten" with a target that exists
// in the repo) or kept out on purpose ("staged" with a note or a deferred field). A rewrite whose target was removed,
// or a staged file nobody explained, is a finding.

/** The promotion states an origin entry may carry. An entry without one is a rewrite (the phase 0 and 1 default). */
export const Promotion = Object.freeze({ Rewritten: 'rewritten', Staged: 'staged' });

/** The finding codes, one per way an origin entry can be wrong. */
export const OriginFindingCode = Object.freeze({
  UnknownPromotion: 'unknown-promotion',
  MissingTarget: 'missing-target',
  UnexplainedStaged: 'unexplained-staged',
  MissingSource: 'missing-source',
  MalformedFile: 'malformed-file',
});

const DEFAULT_PROMOTION = Promotion.Rewritten;
const KNOWN_PROMOTIONS = new Set(Object.values(Promotion));

const hasText = (value) => typeof value === 'string' && value.trim().length > 0;

/**
 * Checks one origins document.
 * @param {string} fileName the origins file name, used in findings
 * @param {unknown} document the parsed JSON
 * @param {(repoRelativePath: string) => boolean} targetExists whether a repo-relative path exists
 * @returns {{ file: string, code: string, detail: string }[]}
 */
export function checkOriginsDocument(fileName, document, targetExists) {
  const origins = document?.origins;
  if (!Array.isArray(origins)) {
    return [{ file: fileName, code: OriginFindingCode.MalformedFile, detail: 'no "origins" array' }];
  }
  return origins.flatMap((origin, index) => checkOrigin({ fileName, origin, index, targetExists }));
}

function checkOrigin({ fileName, origin, index, targetExists }) {
  const where = hasText(origin?.source) ? origin.source : `origins[${index}]`;
  const finding = (code, detail) => ({ file: fileName, code, detail: `${where}: ${detail}` });
  if (!hasText(origin?.source)) return [finding(OriginFindingCode.MissingSource, 'no source path')];
  const promotion = origin.promotion ?? DEFAULT_PROMOTION;
  if (!KNOWN_PROMOTIONS.has(promotion)) {
    return [finding(OriginFindingCode.UnknownPromotion, `promotion "${promotion}" is not one of ${[...KNOWN_PROMOTIONS].join(', ')}`)];
  }
  if (promotion === Promotion.Rewritten) {
    return hasText(origin.target) && targetExists(origin.target)
      ? []
      : [finding(OriginFindingCode.MissingTarget, `rewritten, but its target "${origin.target ?? ''}" does not exist`)];
  }
  return hasText(origin.note) || hasText(origin.deferred)
    ? []
    : [finding(OriginFindingCode.UnexplainedStaged, 'staged without a note or a deferred field')];
}

/**
 * Counts the entries per promotion state, for the CLI summary.
 * @param {unknown[]} documents parsed origins documents
 */
export function countPromotions(documents) {
  const counts = { [Promotion.Rewritten]: 0, [Promotion.Staged]: 0 };
  for (const document of documents) {
    for (const origin of document?.origins ?? []) {
      const promotion = origin?.promotion ?? DEFAULT_PROMOTION;
      if (promotion in counts) counts[promotion] += 1;
    }
  }
  return counts;
}
