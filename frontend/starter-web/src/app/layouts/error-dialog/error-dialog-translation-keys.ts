import { CoreTranslationKeys } from '../../core/i18n/core-translation-keys';

/**
 * The texts of the app error dialog. Title and close come from core (they predate the layout); the reference block
 * texts are the layout's own, under `layouts.error-dialog.*` in both bundles.
 */
export const ErrorDialogTranslationKeys = {
  Title: CoreTranslationKeys.ErrorDialog.Title,
  Close: CoreTranslationKeys.ErrorDialog.Dismiss,
  Reference: 'layouts.error-dialog.reference',
  Copy: 'layouts.error-dialog.copy',
  Copied: 'layouts.error-dialog.copied',
} as const;
