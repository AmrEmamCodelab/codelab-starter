import { ChangeDetectionStrategy, Component, booleanAttribute, input, output } from '@angular/core';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { EditorDialogPatternComponent } from '../../../../patterns';

/**
 * The host page gates where this dialog is placed (`*hasPermission` on the host element), so the inner pattern is
 * marked `data-public-action`: it only answers the page that opened it.
 *
 * Asks before a write that leaves only the break-glass administrator able to manage roles and grants
 * (`ACCESS_LAST_MANAGER`). Confirming resends the write with `confirmBreakGlassOnly: true`.
 */
@Component({
  selector: 'app-last-manager-dialog',
  imports: [EditorDialogPatternComponent, TranslatePipe],
  template: `
    <app-editor-dialog-pattern
      data-public-action
      titleKey="admin.access.last-manager.title"
      confirmKey="admin.access.last-manager.confirm"
      destructive
      [open]="open()"
      [busy]="busy()"
      (confirm)="confirm.emit()"
      (cancel)="cancel.emit()"
    >
      <p class="access-dialog__text">{{ 'errors.ACCESS_LAST_MANAGER' | t }}</p>
      <p class="access-dialog__text">{{ 'admin.access.last-manager.text' | t }}</p>
    </app-editor-dialog-pattern>
  `,
  styleUrl: './access-dialogs.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LastManagerDialogComponent {
  readonly open = input(false, { transform: booleanAttribute });
  readonly busy = input(false, { transform: booleanAttribute });
  readonly confirm = output();
  readonly cancel = output();
}

/** Asks whether to leave an editor with unsaved changes (driven by a `LeaveConfirmation`). */
@Component({
  selector: 'app-leave-dialog',
  imports: [EditorDialogPatternComponent, TranslatePipe],
  template: `
    <app-editor-dialog-pattern
      data-public-action
      titleKey="admin.access.leave.title"
      confirmKey="admin.access.leave.confirm"
      cancelKey="admin.access.leave.cancel"
      destructive
      [open]="open()"
      (confirm)="answer.emit(true)"
      (cancel)="answer.emit(false)"
    >
      <p class="access-dialog__text">{{ 'admin.access.leave.text' | t }}</p>
    </app-editor-dialog-pattern>
  `,
  styleUrl: './access-dialogs.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LeaveDialogComponent {
  readonly open = input(false, { transform: booleanAttribute });
  /** True leaves (discarding the changes), false stays. */
  readonly answer = output<boolean>();
}

/** The warning codes of a successful write, as a polite status notice (`errors.<CODE>` texts). */
@Component({
  selector: 'app-access-warnings',
  imports: [TranslatePipe],
  template: `
    @if (codes().length > 0) {
      <div class="access-notice access-notice--warning" role="status">
        @for (code of codes(); track code) {
          <p class="access-notice__text">{{ 'errors.' + code | t }}</p>
        }
      </div>
    }
  `,
  styleUrl: './access-dialogs.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessWarningsComponent {
  readonly codes = input<readonly string[]>([]);
}
