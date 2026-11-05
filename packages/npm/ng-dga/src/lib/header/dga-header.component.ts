import { afterNextRender, ChangeDetectionStrategy, Component, computed, ElementRef, inject, Injector, input, signal, viewChild } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { createDgaId } from '../internal/unique-id';
import { DgaHeaderKey, type DgaNavItem, exactDgaNavLinks, hasDgaNavChildren, nextDgaMenuIndex, opensDgaSubmenu } from './header-navigation';

const SUBMENU_LINK_SELECTOR = 'a[href]';
/** routerLinkActive options: current only on the link's own URL, or also on the URLs below it. */
const EXACT_MATCH = { exact: true } as const;
const PREFIX_MATCH = { exact: false } as const;

/**
 * The DGA site header: brand (logo and name, linking home), the main navigation with one submenu level, and two
 * content slots, `[dgaHeaderStart]` (before the brand, e.g. a skip link) and `[dgaHeaderActions]` (after the
 * navigation: language switcher, notifications, user menu). Every label arrives translated.
 *
 * Navigation follows the WAI-ARIA disclosure navigation pattern: an entry with children is a button with
 * `aria-expanded` that toggles its submenu. Click toggles it; Enter, Space or ArrowDown opens it and focuses the first
 * link; ArrowDown/ArrowUp/Home/End move between the submenu links; Escape closes it and returns focus to its button.
 * A click outside, or focus leaving the entry, closes it. Leaf entries are router links with `aria-current="page"`;
 * a link another entry links below (a portal home `/portal` next to `/portal/faq`) is current only on its own URL
 * (see `DgaNavItem.exact`), so exactly one entry is the current page.
 *
 * Below the md breakpoint the navigation collapses behind a menu button (`aria-expanded`, `aria-controls`); Escape,
 * on the button or inside the panel, closes it and returns focus to the button, and following a link closes it.
 *
 * @example
 * <dga-header [brandName]="'app.name' | t" logoSrc="branding/logo.svg" homeLink="/"
 *   [navItems]="nav()" [navLabel]="'layout.nav' | t" [menuLabel]="'layout.menu' | t">
 *   <app-language-switcher dgaHeaderActions />
 * </dga-header>
 */
@Component({
  selector: 'dga-header',
  imports: [RouterLink, RouterLinkActive, DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-header.component.html',
  styleUrl: './dga-header.component.css',
  host: {
    class: 'dga-header',
    '(document:click)': 'onDocumentClick($event)',
  },
})
export class DgaHeaderComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly menuButton = viewChild<ElementRef<HTMLButtonElement>>('menuButton');
  private readonly baseId = createDgaId('dga-header');

  /** The product name (already translated); it is also the accessible name of the home link. */
  readonly brandName = input.required<string>();
  /** The logo image; decorative next to the name. Null or empty shows the name only. */
  readonly logoSrc = input<string | null>(null);
  /** Router link of the brand. */
  readonly homeLink = input('/');
  readonly navItems = input<readonly DgaNavItem[]>([]);
  /** Accessible name of the navigation landmark (already translated). */
  readonly navLabel = input.required<string>();
  /** Accessible name of the mobile menu button (already translated). */
  readonly menuLabel = input.required<string>();

  /** The mobile navigation panel is open. */
  readonly menuOpen = signal(false);
  /** The id of the entry whose submenu is open, or null. */
  readonly openItemId = signal<string | null>(null);

  readonly navId = `${this.baseId}-nav`;

  private readonly exactLinks = computed(() => exactDgaNavLinks(this.navItems()));

  /** The routerLinkActive options of a link: exact for a link other entries link below, else prefix matching. */
  activeOptions(link: string): typeof EXACT_MATCH | typeof PREFIX_MATCH {
    return this.exactLinks().has(link) ? EXACT_MATCH : PREFIX_MATCH;
  }

  hasChildren(item: DgaNavItem): boolean {
    return hasDgaNavChildren(item);
  }

  isOpen(item: DgaNavItem): boolean {
    return this.openItemId() === item.id;
  }

  triggerId(itemId: string): string {
    return `${this.baseId}-trigger-${itemId}`;
  }

  submenuId(itemId: string): string {
    return `${this.baseId}-submenu-${itemId}`;
  }

  toggleMenu(): void {
    this.menuOpen.update((open) => !open);
    if (!this.menuOpen()) this.openItemId.set(null);
  }

  toggleSubmenu(item: DgaNavItem): void {
    this.openItemId.set(this.isOpen(item) ? null : item.id);
  }

  /** Following a link closes the submenu and the mobile panel. */
  closeAll(): void {
    this.openItemId.set(null);
    this.menuOpen.set(false);
  }

  onTriggerKeydown(event: KeyboardEvent, item: DgaNavItem): void {
    if (!opensDgaSubmenu(event.key)) return;
    event.preventDefault();
    this.openItemId.set(item.id);
    this.afterRender(() => this.focusSubmenuLink(item, 0));
  }

  onSubmenuKeydown(event: KeyboardEvent, item: DgaNavItem): void {
    const links = this.submenuLinks(item);
    const next = nextDgaMenuIndex(links.length, links.indexOf(this.activeElement() as HTMLAnchorElement), event.key);
    if (next === null) return;
    event.preventDefault();
    links[next]?.focus();
  }

  /** Escape on the menu button closes the mobile panel (focus stays on the button). */
  onMenuButtonKeydown(event: KeyboardEvent): void {
    if (event.key !== DgaHeaderKey.Escape || !this.menuOpen()) return;
    event.preventDefault();
    this.closeAll();
  }

  /** Escape closes the open submenu (focus back to its button), else the mobile panel (focus back to the menu button). */
  onNavKeydown(event: KeyboardEvent): void {
    if (event.key !== DgaHeaderKey.Escape) return;
    const openId = this.openItemId();
    if (openId !== null) {
      event.preventDefault();
      this.openItemId.set(null);
      this.elementById(this.triggerId(openId))?.focus();
      return;
    }
    if (!this.menuOpen()) return;
    event.preventDefault();
    this.menuOpen.set(false);
    this.menuButton()?.nativeElement.focus();
  }

  /** Focus leaving an entry with a submenu closes that submenu. */
  onItemFocusout(event: FocusEvent, item: DgaNavItem): void {
    const next = event.relatedTarget as Node | null;
    const entry = event.currentTarget as HTMLElement;
    if (next !== null && entry.contains(next)) return;
    if (this.isOpen(item)) this.openItemId.set(null);
  }

  onDocumentClick(event: MouseEvent): void {
    if (this.openItemId() === null) return;
    if (this.host.nativeElement.contains(event.target as Node)) return;
    this.openItemId.set(null);
  }

  private submenuLinks(item: DgaNavItem): HTMLAnchorElement[] {
    const submenu = this.elementById(this.submenuId(item.id));
    return submenu === null ? [] : Array.from(submenu.querySelectorAll<HTMLAnchorElement>(SUBMENU_LINK_SELECTOR));
  }

  private focusSubmenuLink(item: DgaNavItem, index: number): void {
    this.submenuLinks(item)[index]?.focus();
  }

  private elementById(id: string): HTMLElement | null {
    return this.host.nativeElement.ownerDocument.getElementById(id);
  }

  private activeElement(): Element | null {
    return this.host.nativeElement.ownerDocument.activeElement;
  }

  private afterRender(action: () => void): void {
    afterNextRender(action, { injector: this.injector });
  }
}
