const DIR_ATTRIBUTE = 'dir';
const RTL = 'rtl';

/**
 * True when the element sits in a right-to-left context: the nearest ancestor (or itself) with a dir attribute
 * says rtl. The app's language service sets dir on <html>, so this follows the active language.
 */
export function isRightToLeft(element: Element): boolean {
  return element.closest(`[${DIR_ATTRIBUTE}]`)?.getAttribute(DIR_ATTRIBUTE)?.toLowerCase() === RTL;
}
