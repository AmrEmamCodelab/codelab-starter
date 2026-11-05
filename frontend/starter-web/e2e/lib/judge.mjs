// The verdict for one screenshot (pure): language check, reference comparison and axe summary in one result row.
import { MASK_COLOUR, Modes, PADDING_COLOUR } from './constants.mjs';
import { compareImages, withinThreshold } from './diff.mjs';
import { ShotStatus, summarizeAxe } from './report.mjs';

/**
 * Judges one captured screenshot.
 * - shot: { id, route, language, width }; capture: { htmlLang, maskRects, axe }.
 * - actual: the decoded screenshot; reference: the decoded baseline or target, or null when there is none.
 * - options: { mode, update, maxDiffRatio, pixelThreshold }; pixelmatch: the pixelmatch function.
 * Returns { result (report row), diff (RGBA image or null) }. The language check wins over the comparison, because a
 * screenshot in the wrong language proves nothing.
 */
export function judgeShot({ shot, capture, actual, reference, options, pixelmatch, files }) {
  const base = {
    id: shot.id,
    route: shot.route.name,
    path: shot.route.path,
    language: shot.language,
    width: shot.width,
    htmlLang: capture.htmlLang,
    masked: capture.maskRects.length,
    maxDiffRatio: options.maxDiffRatio,
    ratio: null,
    diffPixels: null,
    size: { width: actual.width, height: actual.height },
    actual: files.actual,
    reference: files.reference,
    diff: null,
    axe: capture.axe === null ? null : summarizeAxe(capture.axe),
    note: null,
  };
  if (capture.htmlLang !== shot.language) {
    return { result: { ...base, status: ShotStatus.WrongLanguage, note: `html lang is "${capture.htmlLang}", expected "${shot.language}"` }, diff: null };
  }
  if (options.mode === Modes.Baseline && options.update) {
    return { result: { ...base, status: ShotStatus.Updated, note: 'baseline written' }, diff: null };
  }
  if (reference === null) {
    return { result: { ...base, status: ShotStatus.MissingReference, note: `no ${options.mode} image at ${files.reference}` }, diff: null };
  }
  const comparison = compareImages(actual, reference, {
    pixelThreshold: options.pixelThreshold,
    maskRects: capture.maskRects,
    maskColour: MASK_COLOUR,
    paddingColour: PADDING_COLOUR,
    pixelmatch,
  });
  const passed = withinThreshold(comparison.ratio, options.maxDiffRatio);
  const sizeNote = comparison.sizeMatches ? null : `size ${actual.width}x${actual.height} vs reference ${reference.width}x${reference.height}`;
  return {
    result: {
      ...base,
      status: passed ? ShotStatus.Pass : ShotStatus.Fail,
      ratio: comparison.ratio,
      diffPixels: comparison.diffPixels,
      diff: comparison.diffPixels > 0 ? files.diff : null,
      note: sizeNote,
    },
    diff: comparison.diffPixels > 0 ? comparison.diff : null,
  };
}
