import { ChangeDetectionStrategy, Component, ElementRef, inject, signal } from '@angular/core';
import { DgaIconComponent } from '@codelab/ng-dga';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/** Gives each menu instance its own panel id for `aria-controls`. */
let nextMenuId = 0;

/**
 * The signed-in user's name and a menu with logout. Opens on click; closes on a second click, on Escape
 * and on a click outside. Logout goes through {@link AuthService}, which revokes the session and returns
 * to the login page.
 */
@Component({
  selector: 'app-user-profile-menu',
  imports: [DgaIconComponent, TranslatePipe],
  templateUrl: './user-profile-menu.component.html',
  styleUrl: './user-profile-menu.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'close()',
  },
})
export class UserProfileMenuComponent {
  private readonly auth = inject(AuthService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly user = this.auth.user;
  readonly open = signal(false);
  readonly panelId = `app-user-menu-${nextMenuId++}`;

  toggle(): void {
    this.open.update(isOpen => !isOpen);
  }

  close(): void {
    this.open.set(false);
  }

  logout(): Promise<void> {
    this.close();
    return this.auth.logout();
  }

  onDocumentClick(event: Event): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node | null)) {
      this.close();
    }
  }
}
