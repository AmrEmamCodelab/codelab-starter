import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaHeaderKey, DgaIconComponent, nextDgaMenuIndex } from '@codelab/ng-dga';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import {
  badgeText,
  formatNotificationTime,
  MENU_ITEM_LIMIT,
  unreadCount,
  type NotificationItem,
} from './notifications';
import { NotificationsTranslationKeys } from './notifications-translation-keys';

/** The elements of the open panel that arrow keys move between, in document order. */
const PANEL_FOCUSABLE_SELECTOR = 'a[href], button';

/** Gives each menu instance its own ids for `aria-controls` and `aria-labelledby`. */
let nextMenuId = 0;

/**
 * The header bell: an unread badge and a dropdown of the newest notifications (at most {@link MENU_ITEM_LIMIT}), each
 * with a mark-read action, plus a "view all" link to the feed page. Pure presentation: the notifications arrive through
 * `items` and the reader's intent leaves through `markRead`; the shell wires both to the `NotificationsSource`.
 *
 * Disclosure pattern: the bell has `aria-expanded`/`aria-controls`; Enter, Space or a click toggles the panel, and
 * ArrowDown opens it on its first entry; Escape on the bell closes it. Inside the panel ArrowDown/ArrowUp/Home/End move between the links and
 * buttons, Escape closes it and returns focus to the bell, and focus or a click leaving the menu closes it. The unread
 * count is announced in a polite live region whenever it changes.
 */
@Component({
  selector: 'app-notifications-menu',
  imports: [RouterLink, DgaIconComponent, TranslatePipe],
  templateUrl: './notifications-menu.component.html',
  styleUrl: './notifications-menu.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(focusout)': 'onFocusout($event)',
  },
})
export class NotificationsMenuComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly language = inject(LanguageService);
  private readonly bell = viewChild.required<ElementRef<HTMLButtonElement>>('bell');
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');

  /** The reader's notifications, newest first. */
  readonly items = input<readonly NotificationItem[]>([]);
  /** Absolute router link of the full feed page; null hides the "view all" link. */
  readonly viewAllLink = input<string | null>(null);
  /** The id of a notification the reader marked read (or opened while unread). */
  readonly markRead = output<string>();

  protected readonly keys = NotificationsTranslationKeys;
  readonly open = signal(false);
  readonly unread = computed(() => unreadCount(this.items()));
  readonly badge = computed(() => badgeText(this.unread()));
  readonly shown = computed(() => this.items().slice(0, MENU_ITEM_LIMIT));

  private readonly menuId = `app-notifications-${nextMenuId++}`;
  readonly panelId = `${this.menuId}-panel`;
  readonly titleId = `${this.menuId}-title`;

  time(at: string): string {
    return formatNotificationTime(at, this.language.current());
  }

  toggle(): void {
    this.open.update(isOpen => !isOpen);
  }

  close(): void {
    this.open.set(false);
  }

  /**
   * On the bell: Escape closes an open panel (focus stays on the bell); ArrowDown opens the panel and moves focus onto
   * its first entry.
   */
  onBellKeydown(event: KeyboardEvent): void {
    if (event.key === DgaHeaderKey.Escape && this.open()) {
      event.preventDefault();
      this.close();
      return;
    }
    if (event.key !== DgaHeaderKey.ArrowDown) {
      return;
    }
    event.preventDefault();
    this.open.set(true);
    afterNextRender(() => this.focusables()[0]?.focus(), { injector: this.injector });
  }

  onPanelKeydown(event: KeyboardEvent): void {
    if (event.key === DgaHeaderKey.Escape) {
      event.preventDefault();
      this.closeToBell();
      return;
    }
    const entries = this.focusables();
    const current = entries.indexOf(this.host.nativeElement.ownerDocument.activeElement as HTMLElement);
    const next = nextDgaMenuIndex(entries.length, current, event.key);
    if (next === null) {
      return;
    }
    event.preventDefault();
    entries[next]?.focus();
  }

  /** Opening a notification marks it read (when unread) and closes the panel. */
  follow(item: NotificationItem): void {
    if (!item.read) {
      this.markRead.emit(item.id);
    }
    this.close();
  }

  onDocumentClick(event: Event): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node | null)) {
      this.close();
    }
  }

  /** Focus moving out of the menu (Tab past the last entry) closes the panel. */
  onFocusout(event: FocusEvent): void {
    const next = event.relatedTarget as Node | null;
    if (this.open() && next !== null && !this.host.nativeElement.contains(next)) {
      this.close();
    }
  }

  private closeToBell(): void {
    this.close();
    this.bell().nativeElement.focus();
  }

  private focusables(): HTMLElement[] {
    const panel = this.panel()?.nativeElement;
    return panel === undefined ? [] : Array.from(panel.querySelectorAll<HTMLElement>(PANEL_FOCUSABLE_SELECTOR));
  }
}
