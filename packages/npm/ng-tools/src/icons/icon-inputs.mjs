// Where a template names an icon: the `name` input of <dga-icon>, and any `icon` / `<x>Icon` input (leadIcon,
// trailIcon, [icon]) of another component that renders a <dga-icon> for it. Shared by the icon subset and the RTL lint.
import { literalValuesOf } from '../shared/template-values.mjs';

export const ICON_ELEMENT = 'dga-icon';
const ICON_NAME_INPUTS = new Set(['name', '[name]']);
/** Inputs that take an icon name on any element: icon, leadIcon, [trailIcon]... */
const ICON_INPUT = /^\[?(?:icon|\w+Icon)\]?$/;
const ICON_NAME_SHAPE = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;
const BOOLEAN_WORDS = new Set(['true', 'false']);

/** True when attribute `name` of element `element` takes an icon name. */
export function isIconNameInput(element, name) {
  return (element === ICON_ELEMENT && ICON_NAME_INPUTS.has(name)) || ICON_INPUT.test(name);
}

/**
 * The icon names an attribute value gives: the text itself when static, its quoted literals when bound. Only
 * kebab-case words count, and never `true` / `false` (a boolean input such as `hideIcon="true"`).
 */
export function iconNamesOfAttribute(attributeName, value) {
  return literalValuesOf(attributeName, value).filter((candidate) => ICON_NAME_SHAPE.test(candidate) && !BOOLEAN_WORDS.has(candidate));
}
