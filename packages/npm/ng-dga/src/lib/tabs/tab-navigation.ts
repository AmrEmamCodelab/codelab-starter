import type { DgaIconName } from '../icon/dga-icon-names.generated';

export type DgaTabValue = string | number;

/** One tab of a <dga-tabs> group. `label` is already translated by the caller. */
export interface DgaTab {
  readonly value: DgaTabValue;
  readonly label: string;
  readonly icon?: DgaIconName;
  readonly disabled?: boolean;
}

/** The keys a tab list handles (WAI-ARIA tabs pattern, automatic activation). */
export const DgaTabKey = {
  ArrowLeft: 'ArrowLeft',
  ArrowRight: 'ArrowRight',
  Home: 'Home',
  End: 'End',
} as const;

const NOT_FOUND = -1;
const FORWARD = 1;
const BACKWARD = -1;

/** The id of the panel a tab controls, for `panelIdPrefix`: the app gives its panel element this id. */
export function dgaTabPanelId(prefix: string, value: DgaTabValue): string {
  return `${prefix}-${value}`;
}

/** Index of the tab holding the value, or -1. */
export function indexOfTab(tabs: readonly DgaTab[], value: DgaTabValue): number {
  return tabs.findIndex((tab) => tab.value === value);
}

function wrap(index: number, length: number): number {
  return ((index % length) + length) % length;
}

function enabledFrom(tabs: readonly DgaTab[], start: number, step: number): number | null {
  for (let offset = 0; offset < tabs.length; offset += 1) {
    const index = wrap(start + offset * step, tabs.length);
    if (!tabs[index].disabled) return index;
  }
  return null;
}

/** The tab that takes focus in the roving tab order: the selected one, else the first enabled tab. */
export function focusableTabIndex(tabs: readonly DgaTab[], value: DgaTabValue): number | null {
  const selected = indexOfTab(tabs, value);
  if (selected !== NOT_FOUND && !tabs[selected].disabled) return selected;
  return enabledFrom(tabs, 0, FORWARD);
}

/**
 * The tab a key moves to from `current`, skipping disabled tabs and wrapping around, or null for any other key.
 * Arrow keys follow the visual order, so in RTL ArrowLeft moves to the next tab.
 */
export function nextTabIndex(tabs: readonly DgaTab[], current: number, key: string, rightToLeft: boolean): number | null {
  if (tabs.length === 0) return null;
  const forwardKey = rightToLeft ? DgaTabKey.ArrowLeft : DgaTabKey.ArrowRight;
  const backwardKey = rightToLeft ? DgaTabKey.ArrowRight : DgaTabKey.ArrowLeft;
  switch (key) {
    case forwardKey:
      return enabledFrom(tabs, current + FORWARD, FORWARD);
    case backwardKey:
      return enabledFrom(tabs, current + BACKWARD, BACKWARD);
    case DgaTabKey.Home:
      return enabledFrom(tabs, 0, FORWARD);
    case DgaTabKey.End:
      return enabledFrom(tabs, tabs.length - 1, BACKWARD);
    default:
      return null;
  }
}
