import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { AuthService } from '../../core/auth/auth.service';
import { TranslationService } from '../../core/i18n/translation.service';
import { NOTIFICATIONS_SOURCE } from '../../layouts/notifications-menu/notifications';
import { REQUESTER_SHELL_OPTIONS } from '../../layouts/requester-shell/requester-nav';
import { provideShowcaseClock, showcaseClock } from '../showcase-clock';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import {
  NOTIFICATIONS_FIRST_PAGE,
  NOTIFICATIONS_OLDER_PAGE,
  ShowcaseNotificationsSource,
  provideShowcaseNotifications,
} from './showcase-notifications';
import { SAMPLE_USER_ID, SHOWCASE_REQUESTER_OPTIONS, ShowcaseSession, provideShowcaseRequesterFrame } from './showcase-session';

const translate = (key: string) => `T(${key})`;

describe('ShowcaseNotificationsSource', () => {
  it('serves the first page, translated, newest first, timed before the clock', () => {
    const source = new ShowcaseNotificationsSource(translate, showcaseClock);
    const items = source.items();
    expect(items.map(item => item.id)).toEqual(NOTIFICATIONS_FIRST_PAGE.map(entry => entry.id));
    expect(items[0]?.titleText).toBe(`T(${NOTIFICATIONS_FIRST_PAGE[0]?.titleKey})`);
    const times = items.map(item => Date.parse(item.at));
    expect(times).toEqual([...times].sort((a, b) => b - a));
    expect(times.every(time => time < showcaseClock())).toBe(true);
    expect(source.hasMore()).toBe(true);
  });

  it('marks one notification read without touching the others', async () => {
    const source = new ShowcaseNotificationsSource(translate, showcaseClock);
    const unread = source.items().filter(item => !item.read).map(item => item.id);
    await source.markRead(unread[0] ?? '');
    expect(source.items().filter(item => !item.read).map(item => item.id)).toEqual(unread.slice(1));
  });

  it('appends the older page once, then has no more', async () => {
    const source = new ShowcaseNotificationsSource(translate, showcaseClock);
    await source.loadMore();
    await source.loadMore();
    expect(source.items()).toHaveLength(NOTIFICATIONS_FIRST_PAGE.length + NOTIFICATIONS_OLDER_PAGE.length);
    expect(source.hasMore()).toBe(false);
  });

  it('is provided as the NOTIFICATIONS_SOURCE, translating through the app service', () => {
    TestBed.configureTestingModule({
      providers: [provideShowcaseClock(), provideShowcaseNotifications(), { provide: TranslationService, useValue: { translate } }],
    });
    const source = TestBed.inject(NOTIFICATIONS_SOURCE);
    expect(source).toBeInstanceOf(ShowcaseNotificationsSource);
    expect(source.items()[0]?.bodyText).toBe(`T(${NOTIFICATIONS_FIRST_PAGE[0]?.bodyKey})`);
  });
});

describe('the framed requester shell', () => {
  it('shows a signed-in sample user whose name follows the language, and signs out as a no-op', async () => {
    const session = new ShowcaseSession(translate);
    expect(session.isAuthenticated()).toBe(true);
    expect(session.user()).toEqual({ id: SAMPLE_USER_ID, displayName: `T(${ShowcaseTranslationKeys.Layouts.SampleUser})` });
    await expect(session.logout()).resolves.toBeUndefined();
  });

  it('provides the sample session and the showcase navigation', () => {
    TestBed.configureTestingModule({
      providers: [...provideShowcaseRequesterFrame(), { provide: TranslationService, useValue: { translate } }],
    });
    expect(TestBed.inject(AuthService)).toBeInstanceOf(ShowcaseSession);
    expect(TestBed.inject(REQUESTER_SHELL_OPTIONS)).toBe(SHOWCASE_REQUESTER_OPTIONS);
  });

  it('lists only showcase links in its navigation (no permission keys: a sample, not a real menu)', () => {
    const links = SHOWCASE_REQUESTER_OPTIONS.navItems.flatMap(item => [item, ...(item.children ?? [])]);
    expect(links.every(item => item.permission === undefined)).toBe(true);
    expect(links.filter(item => item.link !== undefined).every(item => item.link?.startsWith('/showcase'))).toBe(true);
  });
});
