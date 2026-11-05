import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DgaErrorDialogComponent } from '@codelab/ng-dga';
import { ErrorDialogStore } from '../../core/http/error-dialog.store';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ErrorDialogTranslationKeys } from './error-dialog-translation-keys';

/**
 * Renders the API failure that core's error dialog interceptor recorded in {@link ErrorDialogStore} through the
 * ng-dga `dga-error-dialog`: the translated message for its code, and the reference code the user reads out to
 * support, with a copy button. The dialog is rendered while the store holds a failure; its close button or Escape
 * dismisses it (the store forgets the failure). Hosted once by the root `App`.
 */
@Component({
  selector: 'app-error-dialog',
  imports: [DgaErrorDialogComponent, TranslatePipe],
  templateUrl: './error-dialog.component.html',
  styleUrl: './error-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ErrorDialogComponent {
  readonly store = inject(ErrorDialogStore);
  protected readonly keys = ErrorDialogTranslationKeys;

  dismiss(): void {
    this.store.dismiss();
  }
}
