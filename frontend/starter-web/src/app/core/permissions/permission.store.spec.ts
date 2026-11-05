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
});
