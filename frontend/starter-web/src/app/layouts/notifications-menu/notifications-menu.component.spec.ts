import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  ANY_URL_ROUTES,
  aNotification,
  BOTH_DIRECTIONS,
  fakeAuth,
  layoutTestProviders,
  useDirection,
} from '../layout.testing';
import { MENU_ITEM_LIMIT, type NotificationItem } from './notifications';
import { NotificationsMenuComponent } from './notifications-menu.component';
import { NotificationsTranslationKeys as Keys } from './notifications-translation-keys';

const ITEMS: readonly NotificationItem[] = [
  aNotification({ id: 'n-1', titleText: 'First', read: false, link: '/requests/r-1' }),
  aNotification({ id: 'n-2', titleText: 'Second', read: true }),
  aNotification({ id: 'n-3', titleText: 'Third', read: false }),
];

describe.each(BOTH_DIRECTIONS)('NotificationsMenuComponent (%s)', direction => {
  let fixture: ComponentFixture<NotificationsMenuComponent>;
  let host: HTMLElement;
  let marked: string[];
  let restoreDirection: () => void;

  beforeEach(() => {
    restoreDirection = useDirection(direction);
    TestBed.configureTestingModule({
      imports: [NotificationsMenuComponent],
      providers: [provideRouter(ANY_URL_ROUTES), ...layoutTestProviders(fakeAuth())],
    });
    fixture = TestBed.createComponent(NotificationsMenuComponent);
    fixture.componentRef.setInput('items', ITEMS);
    marked = [];
    fixture.componentInstance.markRead.subscribe(id => marked.push(id));
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);
  });

  afterEach(() => {
    host.remove();
    restoreDirection();
  });

  const bell = () => host.querySelector('.notifications__bell') as HTMLButtonElement;
  const panel = () => host.querySelector('.notifications__panel') as HTMLElement | null;
  const entries = () => [...(panel()?.querySelectorAll<HTMLElement>('a[href], button') ?? [])];

  function press(target: HTMLElement, key: string): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  function openPanel(): void {
    bell().click();
    fixture.detectChanges();
  }

  it('names the bell with the unread count and shows the badge, closed', () => {
    expect(bell().getAttribute('aria-label')).toBe(`${Keys.Bell}|count=2`);
    expect(bell().getAttribute('aria-expanded')).toBe('false');
    expect(host.querySelector('.notifications__badge')?.textContent?.trim()).toBe('2');
    expect(host.querySelector('.notifications__badge')?.getAttribute('aria-hidden')).toBe('true');
    expect(panel()).toBeNull();
  });

  it('announces the unread count politely', () => {
    const live = host.querySelector('[aria-live="polite"]');
    expect(live?.textContent?.trim()).toBe(`${Keys.UnreadStatus}|count=2`);
  });

  it('shows no badge and announces nothing without unread notifications', () => {
    fixture.componentRef.setInput('items', [aNotification({ read: true })]);
    fixture.detectChanges();
    expect(host.querySelector('.notifications__badge')).toBeNull();
    expect(host.querySelector('[aria-live="polite"]')?.textContent?.trim()).toBe('');
  });

  it('opens a labelled panel the bell controls', () => {
    openPanel();
    expect(bell().getAttribute('aria-expanded')).toBe('true');
    expect(panel()?.id).toBe(bell().getAttribute('aria-controls'));
    expect(panel()?.getAttribute('aria-labelledby')).toBe(host.querySelector('.notifications__title')?.id);
    expect([...host.querySelectorAll('.notifications__item-title')].map(title => title.textContent?.trim())).toEqual([
      'First',
      'Second',
      'Third',
    ]);
  });

  it(`lists at most ${MENU_ITEM_LIMIT} notifications`, () => {
    const many = Array.from({ length: MENU_ITEM_LIMIT + 2 }, (_, index) => aNotification({ id: `n-${index}` }));
    fixture.componentRef.setInput('items', many);
    openPanel();
    expect(host.querySelectorAll('.notifications__item')).toHaveLength(MENU_ITEM_LIMIT);
  });

  it('shows the empty state without notifications', () => {
    fixture.componentRef.setInput('items', []);
    openPanel();
    expect(host.querySelector('.notifications__empty')?.textContent?.trim()).toBe(Keys.Empty);
  });

  it('emits mark-read for an unread notification and offers no action on a read one', () => {
    openPanel();
    const buttons = [...host.querySelectorAll<HTMLButtonElement>('.notifications__mark')];
    expect(buttons).toHaveLength(2);
    buttons[1]?.click();
    expect(marked).toEqual(['n-3']);
  });

  it('marks an unread notification read when its link is followed, and closes', () => {
    openPanel();
    (host.querySelector('a.notifications__item-title') as HTMLAnchorElement).click();
    fixture.detectChanges();
    expect(marked).toEqual(['n-1']);
    expect(panel()).toBeNull();
  });

  it('shows the view-all link only when given one', () => {
    openPanel();
    expect(host.querySelector('.notifications__view-all')).toBeNull();
    fixture.componentRef.setInput('viewAllLink', '/notifications');
    fixture.detectChanges();
    expect(host.querySelector('.notifications__view-all')?.getAttribute('href')).toBe('/notifications');
  });

  it('opens on ArrowDown and focuses the first entry', async () => {
    bell().focus();
    const event = press(bell(), 'ArrowDown');
    expect(event.defaultPrevented).toBe(true);
    await fixture.whenStable();
    expect(panel()).not.toBeNull();
    expect(document.activeElement).toBe(entries()[0]);
  });

  it('moves focus with the arrow keys, Home and End, wrapping around', () => {
    openPanel();
    const all = entries();
    all[0]?.focus();
    press(all[0] as HTMLElement, 'ArrowDown');
    expect(document.activeElement).toBe(all[1]);
    press(all[1] as HTMLElement, 'End');
    expect(document.activeElement).toBe(all[all.length - 1]);
    press(all[all.length - 1] as HTMLElement, 'ArrowDown');
    expect(document.activeElement).toBe(all[0]);
    press(all[0] as HTMLElement, 'ArrowUp');
    expect(document.activeElement).toBe(all[all.length - 1]);
    press(all[all.length - 1] as HTMLElement, 'Home');
    expect(document.activeElement).toBe(all[0]);
  });

  it('closes on Escape and returns focus to the bell', () => {
    openPanel();
    const first = entries()[0] as HTMLElement;
    first.focus();
    press(first, 'Escape');
    expect(panel()).toBeNull();
    expect(document.activeElement).toBe(bell());
  });

  it('closes on Escape pressed on the bell itself, keeping focus on the bell', () => {
    bell().focus();
    openPanel();
    const event = press(bell(), 'Escape');
    expect(event.defaultPrevented).toBe(true);
    expect(panel()).toBeNull();
    expect(bell().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(bell());
  });

  it('leaves Escape on a closed bell alone', () => {
    const event = press(bell(), 'Escape');
    expect(event.defaultPrevented).toBe(false);
    expect(panel()).toBeNull();
  });

  it('closes on a click outside, not on a click inside', () => {
    openPanel();
    (panel() as HTMLElement).click();
    fixture.detectChanges();
    expect(panel()).not.toBeNull();
    document.body.click();
    fixture.detectChanges();
    expect(panel()).toBeNull();
  });

  it('closes when focus leaves the menu', () => {
    openPanel();
    const outside = document.createElement('button');
    document.body.appendChild(outside);
    (entries()[0] as HTMLElement).dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: outside }));
    fixture.detectChanges();
    expect(panel()).toBeNull();
    outside.remove();
  });
});
