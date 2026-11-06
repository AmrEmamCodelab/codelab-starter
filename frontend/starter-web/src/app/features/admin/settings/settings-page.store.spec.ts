import { TestBed } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { problem } from '../organizations/admin.testing';
import { AdminSettingsClient } from './admin-settings.client';
import { SettingValueType, type RuntimeSetting } from './setting.models';
import { ALL_GROUPS, SettingsPageStore, compareSettings, settingMatches } from './settings-page.store';
import { runtimeSetting, settingsOfEveryType } from './settings.testing';

describe('settings ordering and search', () => {
  it('orders by group, then key', () => {
    const keys = settingsOfEveryType().sort(compareSettings).map((setting) => setting.key);
    expect(keys).toEqual(['portal.contact.daily-limit', 'portal.welcome-text', 'security.lockout-enabled', 'security.session-idle']);
  });

  it('matches key, group and both descriptions, case-insensitively', () => {
    const setting = runtimeSetting();
    expect(settingMatches(setting, '')).toBe(true);
    expect(settingMatches(setting, 'DAILY')).toBe(true);
    expect(settingMatches(setting, 'portal')).toBe(true);
    expect(settingMatches(setting, 'اليومي')).toBe(true);
    expect(settingMatches(setting, 'Daily limit')).toBe(true);
    expect(settingMatches(setting, 'session')).toBe(false);
  });
});

describe('SettingsPageStore', () => {
  let client: { list: ReturnType<typeof vi.fn>; update: ReturnType<typeof vi.fn> };
  let store: SettingsPageStore;

  beforeEach(() => {
    client = { list: vi.fn().mockReturnValue(of(settingsOfEveryType())), update: vi.fn() };
    TestBed.configureTestingModule({
      providers: [SettingsPageStore, { provide: AdminSettingsClient, useValue: client }],
    });
    store = TestBed.inject(SettingsPageStore);
  });

  it('loads the settings grouped, with the groups for the filter', async () => {
    await store.load();
    expect(store.rows().map((row) => row.group)).toEqual(['portal', 'portal', 'security', 'security']);
    expect(store.groups()).toEqual(['portal', 'security']);
    expect(store.total()).toBe(4);
    expect(store.loading()).toBe(false);
  });

  it('filters by group and search, back to page 1', async () => {
    await store.load();
    store.page.set(2);
    store.filterGroup('security');
    expect(store.page()).toBe(1);
    expect(store.rows().map((row) => row.key)).toEqual(['security.lockout-enabled', 'security.session-idle']);
    store.search.set('idle');
    expect(store.rows().map((row) => row.key)).toEqual(['security.session-idle']);
    store.filterGroup(ALL_GROUPS);
    store.search.set('');
    expect(store.total()).toBe(4);
  });

  it('pages on the client', async () => {
    const many = Array.from({ length: store.pageSize + 3 }, (_, index) =>
      runtimeSetting({ key: `k.${String(index).padStart(3, '0')}` }));
    client.list.mockReturnValue(of(many));
    await store.load();
    expect(store.rows()).toHaveLength(store.pageSize);
    store.page.set(2);
    expect(store.rows().map((row) => row.key)).toEqual(['k.020', 'k.021', 'k.022']);
  });

  it('clears the rows and keeps the refusal when the load fails', async () => {
    await store.load();
    client.list.mockReturnValue(throwError(() => problem(HttpStatus.forbidden, 'FORBIDDEN')));
    await store.load();
    expect(store.rows()).toEqual([]);
    expect(store.error()?.code).toBe('FORBIDDEN');
  });

  it('keeps only the latest load', async () => {
    const slow = new Subject<RuntimeSetting[]>();
    client.list.mockReturnValueOnce(slow).mockReturnValueOnce(of([runtimeSetting({ key: 'newest' })]));
    const first = store.load();
    await store.load();
    slow.next([runtimeSetting({ key: 'stale' })]);
    slow.complete();
    await first;
    expect(store.rows().map((row) => row.key)).toEqual(['newest']);
    expect(store.loading()).toBe(false);
  });

  it('cannot save an unchanged or invalid draft', async () => {
    await store.load();
    expect(store.cannotSave()).toBe(true);
    store.openEdit(runtimeSetting());
    expect(store.cannotSave()).toBe(true);
    store.draft()?.text.set('x');
    expect(store.cannotSave()).toBe(true);
    await store.save();
    expect(client.update).not.toHaveBeenCalled();
    store.draft()?.text.set('25');
    expect(store.cannotSave()).toBe(false);
  });

  it('saves the draft and shows the server answer in its row', async () => {
    await store.load();
    const updated = runtimeSetting({ value: '25', updatedAt: '2026-11-06T08:00:00Z' });
    client.update.mockReturnValue(of(updated));
    store.openEdit(runtimeSetting());
    store.draft()?.text.set('25');
    await store.save();
    expect(client.update).toHaveBeenCalledWith('portal.contact.daily-limit', { value: '25' });
    expect(store.draft()).toBeNull();
    expect(store.rows().find((row) => row.key === updated.key)).toEqual(updated);
  });

  it('keeps the dialog open with the refusal when the save fails', async () => {
    await store.load();
    client.update.mockReturnValue(throwError(() => problem(HttpStatus.badRequest, 'VALIDATION_FAILED', { value: ['INVALID_FORMAT'] })));
    store.openEdit(runtimeSetting({ valueType: SettingValueType.Text, value: 'a' }));
    store.draft()?.text.set('b');
    await store.save();
    expect(store.draft()).not.toBeNull();
    expect(store.saveError()?.fieldErrors).toEqual([{ field: 'value', message: 'INVALID_FORMAT' }]);
    expect(store.saving()).toBe(false);
  });

  it('closes the editor only when not saving', async () => {
    await store.load();
    const pending = new Subject<RuntimeSetting>();
    client.update.mockReturnValue(pending);
    store.openEdit(runtimeSetting());
    store.draft()?.text.set('21');
    const saving = store.save();
    store.closeEditor();
    expect(store.draft()).not.toBeNull();
    pending.next(runtimeSetting({ value: '21' }));
    pending.complete();
    await saving;
    expect(store.draft()).toBeNull();
    store.openEdit(runtimeSetting());
    store.closeEditor();
    expect(store.draft()).toBeNull();
  });
});
