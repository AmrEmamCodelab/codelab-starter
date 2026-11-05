import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { NotificationFilter, type NotificationItem } from '../notifications-menu/notifications';
import { NotificationsTranslationKeys as Keys } from '../notifications-menu/notifications-translation-keys';
import {
  ANY_URL_ROUTES,
  aNotification,
  BOTH_DIRECTIONS,
  fakeAuth,
  layoutTestProviders,
  useDirection,
} from '../layout.testing';
import {
  NOTIFICATION_FILTER_OPTIONS,
  NotificationEmptyKeys,
  NotificationsFeedComponent,
} from './notifications-feed.component';

const ITEMS: readonly NotificationItem[] = [
  aNotification({ id: 'n-1', titleText: 'First', read: false, link: '/requests/r-1' }),
  aNotification({ id: 'n-2', titleText: 'Second', read: true }),
];

describe('NotificationEmptyKeys', () => {
  it('has an empty-state text for every filter option', () => {
    for (const option of NOTIFICATION_FILTER_OPTIONS) {
      expect(NotificationEmptyKeys[option.filter]).toBeTruthy();
    }
  });
});

describe.each(BOTH_DIRECTIONS)('NotificationsFeedComponent (%s)', direction => {
  let fixture: ComponentFixture<NotificationsFeedComponent>;
  let host: HTMLElement;
  let restoreDirection: () => void;

  beforeEach(() => {
    restoreDirection = useDirection(direction);
    TestBed.configureTestingModule({
      imports: [NotificationsFeedComponent],
      providers: [provideRouter(ANY_URL_ROUTES), ...layoutTestProviders(fakeAuth())],
    });
    fixture = TestBed.createComponent(NotificationsFeedComponent);
    fixture.componentRef.setInput('items', ITEMS);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);
  });

  afterEach(() => {
    host.remove();
    restoreDirection();
  });

  const titles = () => [...host.querySelectorAll('.feed__item-title')].map(title => title.textContent?.trim());
  const filterButtons = () => [...host.querySelectorAll<HTMLButtonElement>('.feed__filters button')];

  function choose(filter: NotificationFilter): void {
    const index = NOTIFICATION_FILTER_OPTIONS.findIndex(option => option.filter === filter);
    filterButtons()[index]?.click();
    fixture.detectChanges();
  }

  it('lists every notification, linking the ones with a link, under a labelled filter group', () => {
    expect(titles()).toEqual(['First', 'Second']);
    expect(host.querySelector('a.feed__item-title')?.getAttribute('href')).toBe('/requests/r-1');
    expect(host.querySelector('.feed__filters')?.getAttribute('role')).toBe('group');
    expect(host.querySelector('.feed__filters')?.getAttribute('aria-label')).toBe(Keys.FilterLabel);
  });

  it('marks the active filter pressed and filters the list', () => {
    expect(filterButtons().map(button => button.getAttribute('aria-pressed'))).toEqual(['true', 'false', 'false']);
    choose(NotificationFilter.Unread);
    expect(titles()).toEqual(['First']);
    expect(filterButtons().map(button => button.getAttribute('aria-pressed'))).toEqual(['false', 'true', 'false']);
    choose(NotificationFilter.Read);
    expect(titles()).toEqual(['Second']);
  });

  it('announces the count it lists', () => {
    choose(NotificationFilter.Unread);
    expect(host.querySelector('[aria-live="polite"]')?.textContent?.trim()).toBe(`${Keys.Showing}|count=1`);
  });

  it('shows the empty state of the active filter', () => {
    fixture.componentRef.setInput('items', [aNotification({ read: true })]);
    fixture.detectChanges();
    choose(NotificationFilter.Unread);
    expect(host.querySelector('.feed__empty-text')?.textContent?.trim()).toBe(Keys.EmptyUnread);
    fixture.componentRef.setInput('items', []);
    choose(NotificationFilter.All);
    expect(host.querySelector('.feed__empty-text')?.textContent?.trim()).toBe(Keys.Empty);
  });

  it('emits mark-read for an unread notification only', () => {
    const marked: string[] = [];
    fixture.componentInstance.markRead.subscribe(id => marked.push(id));
    const buttons = [...host.querySelectorAll<HTMLButtonElement>('.feed__item-foot button')];
    expect(buttons).toHaveLength(1);
    buttons[0]?.click();
    expect(marked).toEqual(['n-1']);
  });

  it('offers load-more only while more remain, and emits it', () => {
    expect(host.querySelector('.feed__more')).toBeNull();
    let requests = 0;
    fixture.componentInstance.loadMore.subscribe(() => (requests += 1));
    fixture.componentRef.setInput('hasMore', true);
    fixture.detectChanges();
    (host.querySelector('.feed__more button') as HTMLButtonElement).click();
    expect(requests).toBe(1);
  });

  it('operates the filters from the keyboard (native buttons)', () => {
    const unread = filterButtons()[1] as HTMLButtonElement;
    unread.focus();
    expect(document.activeElement).toBe(unread);
    expect(unread.tagName).toBe('BUTTON');
  });
});
