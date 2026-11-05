import type { DgaIconName } from '../icon/dga-icon-names.generated';

/** The tone of a <dga-confirm-dialog>: `danger` is for destructive or serious actions (delete, reject, deactivate). */
export const DgaConfirmDialogVariant = {
  Default: 'default',
  Danger: 'danger',
} as const;
export type DgaConfirmDialogVariant = (typeof DgaConfirmDialogVariant)[keyof typeof DgaConfirmDialogVariant];

/** Which button receives focus when the dialog opens. */
export const DgaConfirmDialogFocus = {
  Confirm: 'confirm',
  Cancel: 'cancel',
} as const;
export type DgaConfirmDialogFocus = (typeof DgaConfirmDialogFocus)[keyof typeof DgaConfirmDialogFocus];

const VARIANT_ICONS: Readonly<Record<DgaConfirmDialogVariant, DgaIconName>> = {
  [DgaConfirmDialogVariant.Default]: 'question',
  [DgaConfirmDialogVariant.Danger]: 'warning',
};

/** The decorative icon beside the title. */
export function confirmDialogIcon(variant: DgaConfirmDialogVariant): DgaIconName {
  return VARIANT_ICONS[variant];
}

/**
 * The button focused on open: the least destructive choice. A danger dialog focuses Cancel, so a reflexive Enter
 * never deletes; an ordinary one focuses Confirm.
 */
export function confirmDialogInitialFocus(variant: DgaConfirmDialogVariant): DgaConfirmDialogFocus {
  return variant === DgaConfirmDialogVariant.Danger ? DgaConfirmDialogFocus.Cancel : DgaConfirmDialogFocus.Confirm;
}
