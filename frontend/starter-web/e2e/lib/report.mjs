// Results of e2e/visual.mjs: the axe summary, the per-screenshot verdict, the JSON report and its Markdown rendering
// (pure).
import { AXE_BLOCKING_IMPACTS, AXE_IMPACTS } from './constants.mjs';

/** The state of one screenshot. */
export const ShotStatus = Object.freeze({
  /** Within the threshold. */
  Pass: 'pass',
  /** Over the threshold. */
  Fail: 'fail',
  /** Written as the new baseline (--update). */
  Updated: 'updated',
  /** No baseline or target image to compare with. */
  MissingReference: 'missing-reference',
  /** The page did not switch to the requested language (html lang). */
  WrongLanguage: 'wrong-language',
  /** Navigation, screenshot or comparison failed. */
  Error: 'error',
});

const PASSING_STATUSES = new Set([ShotStatus.Pass, ShotStatus.Updated]);
/** How many failing nodes of a violation the report lists. */
export const MAX_TARGETS_PER_VIOLATION = 5;
const PERCENT = 100;
const PIPE = /\|/g;

function cellText(text) {
  return String(text ?? '').replace(PIPE, '\\|');
}
const RATIO_DIGITS = 4;

/** Counts axe violations by impact: { counts, blocking, violations: [{ id, impact, help, helpUrl, nodes, targets }] }. */
export function summarizeAxe(results) {
  const counts = Object.fromEntries(AXE_IMPACTS.map((impact) => [impact, 0]));
  const violations = (results?.violations ?? []).map((violation) => {
    if (violation.impact in counts) counts[violation.impact] += 1;
    return {
      id: violation.id,
      impact: violation.impact ?? null,
      help: violation.help,
      helpUrl: violation.helpUrl ?? null,
      nodes: violation.nodes?.length ?? 0,
      targets: (violation.nodes ?? []).slice(0, MAX_TARGETS_PER_VIOLATION).map((node) => node.target?.join(' ') ?? ''),
    };
  });
  const blocking = AXE_BLOCKING_IMPACTS.reduce((sum, impact) => sum + counts[impact], 0);
  return { counts, blocking, violations };
}

/** Whether one shot passes: an accepted status and no blocking axe violation. */
export function shotPasses(shot) {
  return PASSING_STATUSES.has(shot.status) && (shot.axe === null || shot.axe === undefined || shot.axe.blocking === 0);
}

/**
 * The JSON report: { mode, label, iteration, baseUrl, thresholds, startedAt, finishedAt, passed, totals, shots }.
 * totals: { shots, passed, failed, axeBlocking, worstRatio }.
 */
export function buildReport({ options, shots, startedAt, finishedAt }) {
  const failed = shots.filter((shot) => !shotPasses(shot));
  const ratios = shots.map((shot) => shot.ratio).filter((ratio) => typeof ratio === 'number');
  return {
    mode: options.mode,
    label: options.label,
    iteration: options.iteration,
    baseUrl: options.baseUrl,
    thresholds: { maxDiffRatio: options.maxDiffRatio, pixelThreshold: options.pixelThreshold, axeBlockingImpacts: [...AXE_BLOCKING_IMPACTS] },
    languages: options.languages,
    widths: options.widths,
    maskSelectors: options.maskSelectors,
    startedAt: startedAt.toISOString(),
    finishedAt: finishedAt.toISOString(),
    passed: failed.length === 0 && shots.length > 0,
    totals: {
      shots: shots.length,
      passed: shots.length - failed.length,
      failed: failed.length,
      axeBlocking: shots.reduce((sum, shot) => sum + (shot.axe?.blocking ?? 0), 0),
      worstRatio: ratios.length === 0 ? null : Math.max(...ratios),
    },
    shots,
  };
}

/** A ratio as a percentage with four decimals, or a dash. */
export function formatRatio(ratio) {
  return typeof ratio === 'number' ? `${(ratio * PERCENT).toFixed(RATIO_DIGITS)} %` : '-';
}

function axeCell(axe) {
  if (!axe) return 'skipped';
  return `${axe.counts.critical} critical, ${axe.counts.serious} serious, ${axe.counts.moderate} moderate, ${axe.counts.minor} minor`;
}

/** The Markdown report: summary, one row per screenshot, then every axe violation. */
export function renderMarkdown(report) {
  const lines = [
    '# Visual and accessibility report',
    '',
    `- Result: **${report.passed ? 'PASS' : 'FAIL'}** (${report.totals.passed}/${report.totals.shots} screenshots pass)`,
    `- Mode: ${report.mode}${report.iteration === null ? '' : `, iteration ${report.iteration}`}${report.label ? `, ${report.label}` : ''}`,
    `- App: ${report.baseUrl}`,
    `- Threshold: ${formatRatio(report.thresholds.maxDiffRatio)} of pixels per screenshot, pixelmatch ${report.thresholds.pixelThreshold}; axe fails on ${report.thresholds.axeBlockingImpacts.join(', ')}`,
    `- Masked: ${report.maskSelectors.map((selector) => `\`${selector}\``).join(', ')}`,
    `- Worst difference: ${formatRatio(report.totals.worstRatio)}; blocking axe violations: ${report.totals.axeBlocking}`,
    `- Run: ${report.startedAt} to ${report.finishedAt}`,
    '',
    '| Screenshot | Status | Difference | Accessibility | Note |',
    '| --- | --- | --- | --- | --- |',
    ...report.shots.map((shot) => `| \`${shot.id}\` | ${shotPasses(shot) ? shot.status : `**${shot.status}**`} | ${formatRatio(shot.ratio)} | ${axeCell(shot.axe)} | ${cellText(shot.note)} |`),
  ];
  const violations = report.shots.flatMap((shot) => (shot.axe?.violations ?? []).map((violation) => ({ shot: shot.id, ...violation })));
  if (violations.length > 0) {
    lines.push('', '## Accessibility violations', '', '| Screenshot | Rule | Impact | Nodes | Help | First targets |', '| --- | --- | --- | --- | --- | --- |');
    for (const violation of violations) {
      lines.push(`| \`${violation.shot}\` | \`${violation.id}\` | ${violation.impact ?? '-'} | ${violation.nodes} | ${cellText(violation.help)} | ${violation.targets.map((target) => `\`${target}\``).join(', ')} |`);
    }
  }
  return `${lines.join('\n')}\n`;
}
