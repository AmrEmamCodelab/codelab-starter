import { DGA_ICON_GLYPHS, type DgaIconName } from '../icon/dga-icon-names.generated';

/** The tone of a <dga-alert>. */
export const DgaAlertVariant = {
  Info: 'info',
  Success: 'success',
  Warning: 'warning',
  Error: 'error',
} as const;
export type DgaAlertVariant = (typeof DgaAlertVariant)[keyof typeof DgaAlertVariant];

/** The ARIA live role of an alert: urgent tones interrupt (`alert`), the others are polite (`status`). */
export const DgaAlertRole = {
  Alert: 'alert',
  Status: 'status',
} as const;
export type DgaAlertRole = (typeof DgaAlertRole)[keyof typeof DgaAlertRole];

const URGENT_VARIANTS: ReadonlySet<DgaAlertVariant> = new Set<DgaAlertVariant>([DgaAlertVariant.Error, DgaAlertVariant.Warning]);

/**
 * The icon names each variant tries, in order: its own glyph first, then an older glyph with the same meaning. The
 * first name present in the icon font wins, so the alert keeps an icon while the font gains or renames glyphs.
 */
const VARIANT_ICON_CANDIDATES: Readonly<Record<DgaAlertVariant, readonly string[]>> = {
  [DgaAlertVariant.Info]: ['info'],
  [DgaAlertVariant.Success]: ['success', 'check'],
  [DgaAlertVariant.Warning]: ['warning', 'alert'],
  [DgaAlertVariant.Error]: ['error', 'alert'],
};

/** True when the icon font has a glyph with this name. */
export function isDgaIconName(name: string): name is DgaIconName {
  return Object.hasOwn(DGA_ICON_GLYPHS, name);
}

/** The live role for a variant: `alert` for error and warning, `status` otherwise. */
export function alertRole(variant: DgaAlertVariant): DgaAlertRole {
  return URGENT_VARIANTS.has(variant) ? DgaAlertRole.Alert : DgaAlertRole.Status;
}

/** The icon for a variant, or null when the icon font has none of its candidate glyphs. */
export function alertIcon(variant: DgaAlertVariant): DgaIconName | null {
  return VARIANT_ICON_CANDIDATES[variant].find(isDgaIconName) ?? null;
}
