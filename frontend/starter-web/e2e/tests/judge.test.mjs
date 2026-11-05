// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { Modes } from '../lib/constants.mjs';
import { judgeShot } from '../lib/judge.mjs';
import { ShotStatus } from '../lib/report.mjs';

const SHOT = { id: 'login.ar.390', route: { name: 'login', path: '/login', mask: [] }, language: 'ar', width: 390 };
const FILES = { actual: 'out/actual/login.ar.390.png', reference: 'base/login.ar.390.png', diff: 'out/diff/login.ar.390.png' };
const BASELINE = { mode: Modes.Baseline, update: false, maxDiffRatio: 0.001, pixelThreshold: 0.1 };

function image(width, height, value = 255) {
  return { width, height, data: new Uint8Array(width * height * 4).fill(value) };
}

/** Counts differing pixels by their first byte; enough for these cases. */
function firstByteMatch(left, right, output, width, height) {
  let count = 0;
  for (let index = 0; index < width * height; index += 1) if (left[index * 4] !== right[index * 4]) count += 1;
  return count;
}

function capture(overrides = {}) {
  return { htmlLang: 'ar', maskRects: [], axe: { violations: [] }, ...overrides };
}

function judge(overrides) {
  return judgeShot({ shot: SHOT, capture: capture(), actual: image(10, 10), reference: image(10, 10), options: BASELINE, pixelmatch: firstByteMatch, files: FILES, ...overrides });
}

describe('judgeShot', () => {
  it('passes an identical screenshot and summarises axe', () => {
    const { result, diff } = judge({});
    expect(result).toMatchObject({ id: 'login.ar.390', status: ShotStatus.Pass, ratio: 0, diffPixels: 0, diff: null, axe: { blocking: 0 }, size: { width: 10, height: 10 } });
    expect(diff).toBeNull();
  });

  it('fails over the threshold and returns the diff image', () => {
    const changed = image(10, 10);
    changed.data[0] = 0;
    const { result, diff } = judge({ actual: changed });
    expect(result).toMatchObject({ status: ShotStatus.Fail, ratio: 0.01, diffPixels: 1, diff: FILES.diff });
    expect(diff).not.toBeNull();
  });

  it('notes a size mismatch', () => {
    expect(judge({ actual: image(10, 11) }).result.note).toBe('size 10x11 vs reference 10x10');
  });

  it('reports a page that did not switch language before anything else', () => {
    expect(judge({ capture: capture({ htmlLang: 'en' }) }).result).toMatchObject({ status: ShotStatus.WrongLanguage, note: 'html lang is "en", expected "ar"' });
  });

  it('marks a baseline update and a missing reference', () => {
    expect(judge({ options: { ...BASELINE, update: true }, reference: null }).result.status).toBe(ShotStatus.Updated);
    expect(judge({ reference: null }).result).toMatchObject({ status: ShotStatus.MissingReference, note: 'no baseline image at base/login.ar.390.png' });
  });

  it('skips the axe summary when the scan was off', () => {
    expect(judge({ capture: capture({ axe: null }) }).result.axe).toBeNull();
  });
});
