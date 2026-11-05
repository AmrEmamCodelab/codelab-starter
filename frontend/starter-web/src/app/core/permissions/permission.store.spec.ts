import { computed } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { PermissionStore } from './permission.store';

describe('PermissionStore', () => {
  it('starts empty and unloaded', () => {
    const store = TestBed.inject(PermissionStore);
    expect(store.permissions()).toEqual([]);
    expect(store.loaded()).toBe(false);
    expect(store.has('dashboard.view')).toBe(false);
  });

  it('holds the synced keys, sorted', () => {
    const store = TestBed.inject(PermissionStore);
    store.set(['dashboard.view', 'access.admin.view']);
    expect(store.permissions()).toEqual(['access.admin.view', 'dashboard.view']);
    expect(store.has('dashboard.view')).toBe(true);
    expect(store.loaded()).toBe(true);
  });

  it('marks an empty sync as loaded', () => {
    const store = TestBed.inject(PermissionStore);
    store.set([]);
    expect(store.loaded()).toBe(true);
  });

  it('clears', () => {
    const store = TestBed.inject(PermissionStore);
    store.set(['dashboard.view']);
    store.clear();
    expect(store.has('dashboard.view')).toBe(false);
    expect(store.loaded()).toBe(false);
  });

  it('writes nothing when a sync returns the same keys, so nothing re-renders', () => {
    const store = TestBed.inject(PermissionStore);
    store.set(['a.view', 'b.view']);
    const before = store.held();
    store.set(['b.view', 'a.view']);
    expect(store.held()).toBe(before);
    store.set(['a.view']);
    expect(store.held()).not.toBe(before);
    expect(store.has('b.view')).toBe(false);
  });

  it('re-evaluates a computed has() when a grant or revoke arrives', () => {
    const store = TestBed.inject(PermissionStore);
    const canManage = computed(() => store.has('users.manage'));
    store.set([]);
    expect(canManage()).toBe(false);
    store.set(['users.manage']);
    expect(canManage()).toBe(true);
    store.set([]);
    expect(canManage()).toBe(false);
  });
});
