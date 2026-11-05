// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { Modes } from '../lib/constants.mjs';
import { buildReport, formatRatio, MAX_TARGETS_PER_VIOLATION, renderMarkdown, shotPasses, ShotStatus, summarizeAxe } from '../lib/report.mjs';

const OPTIONS = {
  mode: Modes.Baseline,
  label: 'login',
  iteration: null,
  baseUrl: 'http://localhost:4280',
  maxDiffRatio: 0.001,
  pixelThreshold: 0.1,
  languages: ['ar', 'en'],
  widths: [1440, 390],
  maskSelectors: ['[data-visual-mask]'],
};
const START = new Date('2026-11-05T10:00:00.000Z');
const END = new Date('2026-11-05T10:01:00.000Z');

function violation(id, impact, nodes = 1) {
  return { id, impact, help: `Fix ${id} | now`, helpUrl: `https://example.invalid/${id}`, nodes: Array.from({ length: nodes }, (_, index) => ({ target: [`#n${index}`] })) };
}

describe('summarizeAxe', () => {
  it('counts by impact, blocks on serious and critical, and keeps the first targets', () => {
    const summary = summarizeAxe({ violations: [violation('a', 'serious', 7), violation('b', 'critical'), violation('c', 'minor'), violation('d', 'moderate')] });
    expect(summary.counts).toEqual({ minor: 1, moderate: 1, serious: 1, critical: 1 });
    expect(summary.blocking).toBe(2);
    expect(summary.violations[0]).toMatchObject({ id: 'a', impact: 'serious', nodes: 7 });
    expect(summary.violations[0].targets).toHaveLength(MAX_TARGETS_PER_VIOLATION);
  });

  it('treats missing results as clean', () => {
    expect(summarizeAxe(undefined)).toEqual({ counts: { minor: 0, moderate: 0, serious: 0, critical: 0 }, blocking: 0, violations: [] });
  });
});

describe('shotPasses', () => {
  const clean = summarizeAxe({ violations: [] });
  it.each([
    [{ status: ShotStatus.Pass, axe: clean }, true],
    [{ status: ShotStatus.Updated, axe: null }, true],
    [{ status: ShotStatus.Pass, axe: summarizeAxe({ violations: [violation('x', 'critical')] }) }, false],
    [{ status: ShotStatus.Fail, axe: clean }, false],
    [{ status: ShotStatus.MissingReference, axe: clean }, false],
    [{ status: ShotStatus.WrongLanguage, axe: clean }, false],
    [{ status: ShotStatus.Error, axe: null }, false],
  ])('%j -> %s', (shot, expected) => {
    expect(shotPasses(shot)).toBe(expected);
  });
});

describe('buildReport and renderMarkdown', () => {
  const shots = [
    { id: 'login.ar.1440', status: ShotStatus.Pass, ratio: 0.0004, axe: summarizeAxe({ violations: [violation('region', 'moderate')] }), note: null },
    { id: 'login.en.390', status: ShotStatus.Fail, ratio: 0.02, axe: summarizeAxe({ violations: [violation('color-contrast', 'serious')] }), note: 'size 390x900 vs reference 390x880' },
  ];

  it('totals the run and fails when any shot fails', () => {
    const report = buildReport({ options: OPTIONS, shots, startedAt: START, finishedAt: END });
    expect(report.passed).toBe(false);
    expect(report.totals).toEqual({ shots: 2, passed: 1, failed: 1, axeBlocking: 1, worstRatio: 0.02 });
    expect(report.thresholds).toEqual({ maxDiffRatio: 0.001, pixelThreshold: 0.1, axeBlockingImpacts: ['serious', 'critical'] });
    expect(report.startedAt).toBe('2026-11-05T10:00:00.000Z');
  });

  it('never passes an empty run', () => {
    expect(buildReport({ options: OPTIONS, shots: [], startedAt: START, finishedAt: END }).passed).toBe(false);
  });

  it('renders a summary, a row per screenshot and every violation with escaped pipes', () => {
    const markdown = renderMarkdown(buildReport({ options: OPTIONS, shots, startedAt: START, finishedAt: END }));
    expect(markdown).toContain('- Result: **FAIL** (1/2 screenshots pass)');
    expect(markdown).toContain('- Threshold: 0.1000 % of pixels per screenshot, pixelmatch 0.1; axe fails on serious, critical');
    expect(markdown).toContain('| `login.ar.1440` | pass | 0.0400 % | 0 critical, 0 serious, 1 moderate, 0 minor |  |');
    expect(markdown).toContain('| `login.en.390` | **fail** | 2.0000 % |');
    expect(markdown).toContain('| `login.en.390` | `color-contrast` | serious | 1 | Fix color-contrast \\| now | `#n0` |');
    expect(markdown.endsWith('\n')).toBe(true);
  });

  it('formats ratios as percentages', () => {
    expect(formatRatio(0.05)).toBe('5.0000 %');
    expect(formatRatio(null)).toBe('-');
  });
});
