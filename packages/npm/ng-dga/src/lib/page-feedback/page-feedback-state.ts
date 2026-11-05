import type { DgaIconName } from '../icon/dga-icon-names.generated';

/** The outcome a <dga-page-feedback> banner reports. */
export const DgaPageFeedbackKind = {
  Success: 'success',
  Error: 'error',
  Info: 'info',
} as const;
export type DgaPageFeedbackKind = (typeof DgaPageFeedbackKind)[keyof typeof DgaPageFeedbackKind];

/** The live role of the banner: an error interrupts (`alert`), success and info are polite (`status`). */
export const DgaPageFeedbackRole = {
  Alert: 'alert',
  Status: 'status',
} as const;
export type DgaPageFeedbackRole = (typeof DgaPageFeedbackRole)[keyof typeof DgaPageFeedbackRole];

/**
 * The page-level outcome to show (texts already translated). A new object is a new message: the banner moves focus
 * to itself each time the caller sets one, so set a fresh object for every outcome, even a repeated one.
 */
export interface DgaPageFeedbackState {
  readonly kind: DgaPageFeedbackKind;
  readonly message: string;
  /** Optional bold first line. */
  readonly title?: string | null;
}

const KIND_ICONS: Readonly<Record<DgaPageFeedbackKind, DgaIconName>> = {
  [DgaPageFeedbackKind.Success]: 'success',
  [DgaPageFeedbackKind.Error]: 'error',
  [DgaPageFeedbackKind.Info]: 'info',
};

export function pageFeedbackRole(kind: DgaPageFeedbackKind): DgaPageFeedbackRole {
  return kind === DgaPageFeedbackKind.Error ? DgaPageFeedbackRole.Alert : DgaPageFeedbackRole.Status;
}

export function pageFeedbackIcon(kind: DgaPageFeedbackKind): DgaIconName {
  return KIND_ICONS[kind];
}
