// Every text colour token reaches WCAG AA (4.5:1) on the surfaces it is drawn on. The tokens are read from
// styles/_tokens.scss and resolved through var() the same way the tokens document is generated, so a palette change
// that drops a pair below AA fails here before axe finds it on a page.
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { parseTokenSource, resolveValue, TOKEN_SOURCES } from '../scripts/generate-tokens-doc.mjs';

/** WCAG 2.2 success criterion 1.4.3: the minimum contrast of normal-size text. */
const AA_NORMAL_TEXT = 4.5;
const HEX_RGB = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i;
const HEX_RADIX = 16;
const CHANNEL_MAX = 255;
/** sRGB to linear light (WCAG relative luminance). */
const LINEAR_THRESHOLD = 0.04045;
const LINEAR_DIVISOR = 12.92;
const GAMMA_OFFSET = 0.055;
const GAMMA_SCALE = 1.055;
const GAMMA = 2.4;
const LUMINANCE_WEIGHTS = [0.2126, 0.7152, 0.0722];
const FLARE = 0.05;

/** The text tokens and the surfaces each must stay readable on (the default surface plus the ones it is used on). */
const PAIRS = Object.freeze([
  ...['default', 'display', 'paragraph', 'secondary', 'muted', 'placeholder', 'link', 'link-hover', 'primary', 'error'].flatMap(
    (text) => ['default', 'muted'].map((surface) => [`--dga-color-text-${text}`, `--dga-color-surface-${surface}`]),
  ),
  ['--dga-color-text-secondary', '--dga-color-surface-subtle'],
  ['--dga-color-text-muted', '--dga-color-surface-subtle'],
  ['--dga-color-text-muted', '--dga-color-surface-disabled'],
  ['--dga-color-text-primary', '--dga-color-primary-subtle'],
  ['--dga-color-text-on-color', '--dga-color-surface-inverse'],
  ['--dga-color-text-on-primary', '--dga-color-primary'],
]);

function resolvedTokens() {
  const [tokenSource] = TOKEN_SOURCES;
  const { tokens } = parseTokenSource(readFileSync(tokenSource.file, 'utf8'), '_tokens.scss');
  const raw = new Map(tokens.map((token) => [token.name, token.value]));
  return new Map(tokens.map((token) => [token.name, resolveValue(token.value, raw)]));
}

function luminance(hex) {
  const match = HEX_RGB.exec(hex);
  if (match === null) throw new Error(`Not a #RRGGBB colour: ${hex}`);
  const linear = match.slice(1).map((channel) => {
    const value = Number.parseInt(channel, HEX_RADIX) / CHANNEL_MAX;
    return value <= LINEAR_THRESHOLD ? value / LINEAR_DIVISOR : ((value + GAMMA_OFFSET) / GAMMA_SCALE) ** GAMMA;
  });
  return linear.reduce((sum, value, index) => sum + value * LUMINANCE_WEIGHTS[index], 0);
}

function contrastRatio(foreground, background) {
  const [lighter, darker] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  return (lighter + FLARE) / (darker + FLARE);
}

describe('contrastRatio', () => {
  it('matches the WCAG reference values', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contrastRatio('#ffffff', '#ffffff')).toBeCloseTo(1, 5);
    expect(contrastRatio('#9da4ae', '#ffffff')).toBeLessThan(AA_NORMAL_TEXT);
  });
});

describe('text colour tokens', () => {
  const tokens = resolvedTokens();

  it.each(PAIRS)('%s on %s reaches AA', (text, surface) => {
    expect(contrastRatio(tokens.get(text), tokens.get(surface))).toBeGreaterThanOrEqual(AA_NORMAL_TEXT);
  });
});
