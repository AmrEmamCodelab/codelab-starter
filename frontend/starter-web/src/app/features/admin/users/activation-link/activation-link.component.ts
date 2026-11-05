import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DgaButtonComponent } from '@codelab/ng-dga';
import { LanguageService } from '../../../../core/i18n/language.service';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { ClipboardService } from './clipboard.service';
import { formatInstant } from './format-instant';

/** The outcome of the last copy. */
export enum CopyState {
  Idle = 1,
  Copied = 2,
  Failed = 3,
}

/**
 * An activation link the admin hands to the user: the link in a read-only field (selectable for a manual copy), a
 * copy button and the expiry in the current language. The link carries a one-time sealed ticket in its fragment:
 * it is shown, never logged or stored. Copying is display-only (no server call), so the button is a public action;
 * the screens that show a link are gated by `users.manage` or `users.import`.
 */
@Component({
  selector: 'app-activation-link',
  imports: [DgaButtonComponent, TranslatePipe],
  templateUrl: './activation-link.component.html',
  styleUrl: './activation-link.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivationLinkComponent {
  private readonly clipboard = inject(ClipboardService);
  private readonly language = inject(LanguageService);

  readonly link = input.required<string>();
  /** ISO-8601 instant the link stops working. */
  readonly expiresAt = input.required<string>();
  /** Translation key of the field's label. */
  readonly labelKey = input('admin.activation.link');

  protected readonly CopyState = CopyState;
  protected readonly copyState = signal(CopyState.Idle);
  protected readonly expiry = computed(() => formatInstant(this.expiresAt(), this.language.current()));
  protected readonly fieldId = `app-activation-link-${nextLinkId()}`;

  async copy(): Promise<void> {
    const copied = await this.clipboard.copy(this.link());
    this.copyState.set(copied ? CopyState.Copied : CopyState.Failed);
  }

  /** Selects the whole link when the field gets focus, ready for a manual copy. */
  selectAll(event: Event): void {
    (event.target as HTMLInputElement | null)?.select();
  }
}

let linkCount = 0;

function nextLinkId(): number {
  linkCount += 1;
  return linkCount;
}
