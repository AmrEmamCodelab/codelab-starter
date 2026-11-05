import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DgaButtonComponent, DgaIconComponent } from '@codelab/ng-dga';
import { ErrorDialogStore } from '../../core/http/error-dialog.store';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/**
 * Renders the API failure that core's error dialog interceptor recorded in {@link ErrorDialogStore}: the
 * translated message for its code and the reference code the user reads out to support. Escape or the
 * dismiss button closes it.
 */
@Component({
  selector: 'app-error-dialog',
  imports: [DgaButtonComponent, DgaIconComponent, TranslatePipe],
  templateUrl: './error-dialog.component.html',
  styleUrl: './error-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:keydown.escape)': 'dismiss()',
  },
})
export class ErrorDialogComponent {
  readonly store = inject(ErrorDialogStore);
  readonly titleId = 'app-error-dialog-title';
  readonly messageId = 'app-error-dialog-message';

  dismiss(): void {
    this.store.dismiss();
  }
}
