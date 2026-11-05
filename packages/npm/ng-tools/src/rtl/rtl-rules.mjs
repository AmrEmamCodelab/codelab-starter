// The RTL lint rules (base rule base.fe-tokens-logical-css, the template and TypeScript half; Stylelint covers .css and
// .scss): an Arabic-first app lays out right to left, so templates and TypeScript never name a physical side.
//
// What is refused, in templates (`.html` and inline `template:` strings) and in TypeScript:
// - a style binding on a physical side: [style.margin-left], [style.left.px], host '[style.right]', el.style.marginLeft;
// - a physical side in style text: style="padding-right: 4px", [ngStyle]="{ 'margin-left': x }", setStyle(el, 'left', ..);
// - a physical alignment: text-align, float or clear set to left or right;
// - a hard-coded direction: dir="ltr" / dir="rtl" (dir="auto" and a bound direction are fine), setAttribute('dir', 'rtl');
// - icons: a physically named icon (arrow-left), mirroring switched off on an icon that mirrors in RTL
//   (flipInRtl="false"), and, in the icon map, a directional icon (chevron-start, logout) that does not mirror.
// An element carrying `data-fixed-direction` (a phone number, an IBAN, code) is exempt from the direction and icon
// rules; whole files can be exempted from the direction rule (the host page that sets the document direction).
import { ICON_ELEMENT, iconNamesOfAttribute, isIconNameInput } from '../icons/icon-inputs.mjs';
import { scanTemplate } from '../permission-gates/template-scanner.mjs';

export const RtlFindingKinds = Object.freeze({
  physicalStyleBinding: 'physical-style-binding',
  physicalStyle: 'physical-style',
  physicalAlignment: 'physical-alignment',
  hardCodedDirection: 'hard-coded-direction',
  physicalIconName: 'physical-icon-name',
  iconMirrorDisabled: 'icon-mirror-disabled',
  iconNotMirrored: 'icon-not-mirrored',
});

/** The attribute that exempts one element (and only it) from the direction and icon rules. */
export const FIXED_DIRECTION_MARKER = 'data-fixed-direction';

export const RTL_LINT_FIX =
  'Use logical CSS in the component stylesheet (margin-inline-start, inset-inline-end, text-align: start), logical icon names (chevron-start, arrow-end), and let the app set the document direction; mark a deliberately fixed-direction element with data-fixed-direction.';

/** Names a physical side: margin-left, border-top-right-radius, inset-right... (kebab case). */
const KEBAB_SIDE = /(?<![\w-])(?:(?:margin|padding|border|inset|scroll-margin|scroll-padding)-(?:left|right)(?:-[a-z]+)*|border-(?:top|bottom)-(?:left|right)-radius)(?![\w-])/g;
/** The camelCase forms: marginLeft, borderTopRightRadius, paddingRight... */
const CAMEL_SIDE = /(?<![\w$])(?:margin|padding|border|inset|scrollMargin|scrollPadding)(?:Top|Bottom)?(?:Left|Right)\w*/g;
/** A CSS declaration of the `left` or `right` offset: `left: 0`. */
const BARE_OFFSET_DECLARATION = /(?<![\w-])(?:left|right)\s*:/g;
/** A quoted `left` / `right` key of a style object: { 'left.px': x }. */
const QUOTED_OFFSET_KEY = /['"](?:left|right)(?:\.[\w%]+)?['"]\s*:/g;
/** text-align, float or clear set to a physical side, in CSS text. */
const KEBAB_ALIGNMENT = /(?<![\w-])(?:text-align|float|clear)\s*:\s*(?:left|right)\b/g;
/** The same in TypeScript or a style object: textAlign: 'left', el.style.cssFloat = 'right'. */
const CAMEL_ALIGNMENT = /(?<![\w$])['"]?(?:textAlign|cssFloat|float|clear|text-align)['"]?\s*[:=]\s*['"`](?:left|right)['"`]/g;
/** A style binding name: [style.margin-left], [style.left.px]; group 1 is the property. */
const STYLE_BINDING = /\[style\.([\w-]+)(?:\.[\w%]+)?\]/g;
const STYLE_BINDING_NAME = /^\[style\.([\w-]+)(?:\.[\w%]+)?\]$/;
/** A DOM style property: el.style.marginLeft; group 1 is the property. */
const DOM_STYLE_PROPERTY = /\.style\.([A-Za-z]+)\b/g;
const PHYSICAL_PROPERTY_NAME = /^(?:left|right|float|cssFloat|(?:margin|padding|border|inset|scroll-margin|scroll-padding)-(?:left|right)(?:-[a-z]+)*|border-(?:top|bottom)-(?:left|right)-radius|(?:margin|padding|border|inset|scrollMargin|scrollPadding)(?:Top|Bottom)?(?:Left|Right)\w*)$/;
const ALIGNMENT_PROPERTY_NAME = /^(?:text-align|textAlign)$/;
const QUOTED_PHYSICAL_SIDE = /['"`](?:left|right)['"`]/;
/** A hard-coded direction in TypeScript: setAttribute('dir', 'rtl'), element.dir = 'ltr'. */
const SCRIPT_DIRECTION = /setAttribute\(\s*['"]dir['"]\s*,\s*['"](?:ltr|rtl)['"]|\.dir\s*=\s*['"](?:ltr|rtl)['"]/g;
const FIXED_DIRECTIONS = new Set(['ltr', 'rtl']);
const QUOTED_FIXED_DIRECTION = /^\s*['"](?:ltr|rtl)['"]\s*$/;
const DIRECTION_BINDINGS = new Set(['[dir]', '[attr.dir]']);
const STYLE_TEXT_ATTRIBUTES = new Set(['style', '[style]', '[attr.style]', '[ngStyle]']);
const STATIC_STYLE_ATTRIBUTE = 'style';
const DIRECTION_ATTRIBUTE = 'dir';
/** An icon named after a physical side: arrow-left, chevron-right. */
const PHYSICAL_ICON_NAME = /(?:^|-)(?:left|right)(?:-|$)/;
/** An icon pointing along the reading direction: chevron-start, arrow-end. */
const DIRECTIONAL_ICON_NAME = /(?:^|-)(?:start|end)(?:-|$)/;
/** Directional icons whose names do not say so (they point out of or into the reading flow). */
export const DIRECTIONAL_ICON_NAMES = Object.freeze(['logout', 'login']);
const FLIP_ATTRIBUTES = Object.freeze(['flipInRtl', '[flipInRtl]']);
const FALSE_VALUE = 'false';
const FIRST_GROUP = 1;
/** A string literal's content starts one character after its opening quote. */
const QUOTE_LENGTH = 1;
const OPEN_EVENT = 'open';

/** True when an icon points along the reading direction and so must mirror in RTL. */
export function isDirectionalIcon(name) {
  return DIRECTIONAL_ICON_NAME.test(name) || DIRECTIONAL_ICON_NAMES.includes(name);
}

/** True when a style property name (kebab or camel case) names a physical side or float. */
export function isPhysicalProperty(name) {
  return PHYSICAL_PROPERTY_NAME.test(name);
}

function matchesOf(pattern, text) {
  return [...text.matchAll(pattern)].map((match) => ({ start: match.index, end: match.index + match[0].length, text: match[0], groups: match }));
}

/** Keeps the first of any overlapping matches (the list order is the priority), sorted by offset. */
function withoutOverlaps(matches) {
  const kept = [];
  for (const match of matches) {
    if (!kept.some((other) => match.start < other.end && other.start < match.end)) kept.push(match);
  }
  return kept.sort((first, second) => first.start - second.start);
}

/**
 * Physical sides and alignments in a piece of style text (a style attribute, a style object or a TypeScript string).
 * `expression` also looks for camelCase and quoted object keys. Returns `{ kind, detail, start, end }` per match
 * (offsets in `text`), without overlaps.
 */
export function findPhysicalStyleText(text, { expression = false } = {}) {
  const sides = [KEBAB_SIDE, BARE_OFFSET_DECLARATION, ...(expression ? [CAMEL_SIDE, QUOTED_OFFSET_KEY] : [])];
  const alignments = [KEBAB_ALIGNMENT, ...(expression ? [CAMEL_ALIGNMENT] : [])];
  return withoutOverlaps([
    ...alignments.flatMap((pattern) => matchesOf(pattern, text)).map((match) => ({ ...match, kind: RtlFindingKinds.physicalAlignment })),
    ...sides.flatMap((pattern) => matchesOf(pattern, text)).map((match) => ({ ...match, kind: RtlFindingKinds.physicalStyle })),
  ]).map(({ kind, text: detail, start, end }) => ({ kind, detail: detail.trim(), start, end }));
}

function styleBindingFindings(name, value) {
  const binding = STYLE_BINDING_NAME.exec(name);
  if (binding === null) return [];
  const property = binding[FIRST_GROUP];
  if (isPhysicalProperty(property)) return [{ kind: RtlFindingKinds.physicalStyleBinding, detail: name }];
  if (ALIGNMENT_PROPERTY_NAME.test(property) && QUOTED_PHYSICAL_SIDE.test(value ?? '')) {
    return [{ kind: RtlFindingKinds.physicalAlignment, detail: `${name}="${value}"` }];
  }
  return [];
}

function styleTextFindings(name, value) {
  if (!STYLE_TEXT_ATTRIBUTES.has(name) || value === null) return [];
  return findPhysicalStyleText(value, { expression: name !== STATIC_STYLE_ATTRIBUTE }).map(({ kind, detail }) => ({ kind, detail }));
}

function directionFindings(name, value) {
  const fixed = name === DIRECTION_ATTRIBUTE
    ? FIXED_DIRECTIONS.has((value ?? '').trim().toLowerCase())
    : DIRECTION_BINDINGS.has(name) && QUOTED_FIXED_DIRECTION.test(value ?? '');
  return fixed ? [{ kind: RtlFindingKinds.hardCodedDirection, detail: `${name}="${value}"` }] : [];
}

function iconFindings(element, attributes, mirroredIcons) {
  const findings = [];
  const flipOff = FLIP_ATTRIBUTES.some((flip) => attributes.get(flip)?.trim() === FALSE_VALUE);
  for (const [name, value] of attributes) {
    if (!isIconNameInput(element, name)) continue;
    for (const icon of iconNamesOfAttribute(name, value)) {
      if (PHYSICAL_ICON_NAME.test(icon)) findings.push({ kind: RtlFindingKinds.physicalIconName, detail: icon });
      else if (element === ICON_ELEMENT && flipOff && (mirroredIcons?.has(icon) ?? isDirectionalIcon(icon))) {
        findings.push({ kind: RtlFindingKinds.iconMirrorDisabled, detail: icon });
      }
    }
  }
  return findings;
}

/**
 * RTL findings in an Angular template, as `{ kind, line, column, element, detail }` (the position of the element).
 * @param {string} text the template
 * @param {{ mirroredIcons?: Set<string> | null, allowDirection?: boolean }} [options] `mirroredIcons`: the icons that
 *   mirror in RTL (default: the directional names); `allowDirection`: the file may fix the direction (the host page)
 */
export function findRtlTemplateFindings(text, { mirroredIcons = null, allowDirection = false } = {}) {
  const findings = [];
  for (const event of scanTemplate(text)) {
    if (event.type !== OPEN_EVENT) continue;
    const exempt = event.attributes.has(FIXED_DIRECTION_MARKER);
    const own = [...event.attributes].flatMap(([name, value]) => [
      ...styleBindingFindings(name, value),
      ...styleTextFindings(name, value),
      ...(exempt || allowDirection ? [] : directionFindings(name, value)),
    ]);
    const icons = exempt ? [] : iconFindings(event.name, event.attributes, mirroredIcons);
    for (const finding of [...own, ...icons]) findings.push({ ...finding, line: event.line, column: event.column, element: event.name });
  }
  return findings;
}

/**
 * RTL findings in TypeScript, as `{ kind, offset, detail }`. `code` is the source with comments and inline templates
 * blanked; `strings` are its remaining string literals (from lexScript).
 */
export function findRtlScriptFindings({ code, strings }, { allowDirection = false } = {}) {
  const bindings = [
    ...matchesOf(STYLE_BINDING, code).filter((match) => isPhysicalProperty(match.groups[FIRST_GROUP])),
    ...matchesOf(DOM_STYLE_PROPERTY, code).filter((match) => isPhysicalProperty(match.groups[FIRST_GROUP])),
  ].map((match) => ({ ...match, kind: RtlFindingKinds.physicalStyleBinding }));
  const alignments = matchesOf(CAMEL_ALIGNMENT, code).map((match) => ({ ...match, kind: RtlFindingKinds.physicalAlignment }));
  const directions = allowDirection ? [] : matchesOf(SCRIPT_DIRECTION, code).map((match) => ({ ...match, kind: RtlFindingKinds.hardCodedDirection }));
  const inStrings = strings.flatMap(({ offset, raw }) => findPhysicalStyleText(raw).map((finding) => ({
    kind: finding.kind,
    text: finding.detail,
    start: offset + QUOTE_LENGTH + finding.start,
    end: offset + QUOTE_LENGTH + finding.end,
  })));
  const sides = [...matchesOf(KEBAB_SIDE, code), ...matchesOf(CAMEL_SIDE, code)].map((match) => ({ ...match, kind: RtlFindingKinds.physicalStyle }));
  return withoutOverlaps([...bindings, ...alignments, ...directions, ...inStrings, ...sides])
    .map(({ kind, text, start }) => ({ kind, offset: start, detail: text.trim() }));
}

/**
 * Directional icons the icon map does not mirror. `iconMap` is the shipped code point map
 * (`{ icons: { <name>: { mirrorInRtl } } }`); returns `{ kind, detail }` per icon.
 */
export function findUnmirroredIcons(iconMap) {
  return Object.entries(iconMap?.icons ?? {})
    .filter(([name, icon]) => isDirectionalIcon(name) && icon?.mirrorInRtl !== true)
    .map(([name]) => ({ kind: RtlFindingKinds.iconNotMirrored, detail: name }));
}

/** The icons the map mirrors in RTL. */
export function mirroredIconsOf(iconMap) {
  return new Set(Object.entries(iconMap?.icons ?? {}).filter(([, icon]) => icon?.mirrorInRtl === true).map(([name]) => name));
}
