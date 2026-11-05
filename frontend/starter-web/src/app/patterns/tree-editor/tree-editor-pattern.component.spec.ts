import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { patternTestProviders, textOf } from '../patterns.testing';
import type { TreeLabelResolver, TreeNode, TreeSelectionChange } from './key-tree';
import { TreeEditorPatternComponent } from './tree-editor-pattern.component';

const PERMISSIONS = ['access.roles.view', 'access.roles.manage', 'access.grants.view', 'users.view'];

/** Living documentation: a role editor's permission tree. */
@Component({
  imports: [TreeEditorPatternComponent],
  template: `
    <app-tree-editor-pattern
      labelKey="admin.access.roles.permissions"
      [keys]="keys"
      [nodes]="nodes()"
      [labelFor]="labelFor"
      [readOnly]="readOnly()"
      [(value)]="selected"
      (selectionChange)="changes.push($event)"
    />
  `,
})
class RoleEditorHost {
  readonly keys = PERMISSIONS;
  readonly nodes = signal<readonly TreeNode[] | null>(null);
  readonly labelFor: TreeLabelResolver = (key) => (key === 'users' ? { text: 'Users' } : { textKey: `permissions.${key}` });
  readonly readOnly = signal(false);
  readonly selected = signal<ReadonlySet<string>>(new Set(['access.roles.view']));
  readonly changes: TreeSelectionChange[] = [];
}

describe('TreeEditorPatternComponent', () => {
  let fixture: ComponentFixture<RoleEditorHost>;
  let host: HTMLElement;
  let screen: RoleEditorHost;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [RoleEditorHost], providers: patternTestProviders() });
    fixture = TestBed.createComponent(RoleEditorHost);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    screen = fixture.componentInstance;
  });

  const item = (key: string) => host.querySelector(`li[data-key="${key}"]`) as HTMLElement;
  const checkbox = (key: string) => item(key).querySelector(':scope > .tree-editor__row input[type="checkbox"]') as HTMLInputElement;
  const toggleButton = (key: string) => item(key).querySelector(':scope > .tree-editor__row button') as HTMLButtonElement;
  const toolbarButton = (index: number) => host.querySelectorAll<HTMLButtonElement>('.tree-editor__actions dga-button button')[index];

  function click(input: HTMLInputElement): void {
    input.click();
    fixture.detectChanges();
  }

  function filter(text: string): void {
    const input = host.querySelector<HTMLInputElement>('input[type="search"]') as HTMLInputElement;
    input.value = text;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('renders the tree in a labelled fieldset with resolved labels', () => {
    expect(textOf(host, 'legend')).toBe('admin.access.roles.permissions');
    expect(item('access')).not.toBeNull();
    expect(item('access.roles.view')).not.toBeNull();
    expect(textOf(item('access.roles.view'), '.dga-checkbox__label')).toBe('permissions.access.roles.view');
    expect(textOf(item('users'), ':scope > .tree-editor__row .dga-checkbox__label')).toBe('Users');
    expect(textOf(host, '.tree-editor__count')).toBe('patterns.tree.selected-count|selected=1,total=4');
  });

  it('shows parents as mixed or checked from the leaves', () => {
    expect(checkbox('access.roles.view').checked).toBe(true);
    expect(checkbox('access.roles').indeterminate).toBe(true);
    expect(checkbox('access').indeterminate).toBe(true);
    expect(checkbox('users').indeterminate).toBe(false);
    expect(checkbox('users').checked).toBe(false);
  });

  it('checks a whole subtree from its parent and reports what changed', () => {
    click(checkbox('access'));
    expect([...screen.selected()].sort()).toEqual(['access.grants.view', 'access.roles.manage', 'access.roles.view']);
    expect([...screen.changes[0].added].sort()).toEqual(['access.grants.view', 'access.roles.manage']);
    expect(screen.changes[0].removed.size).toBe(0);
    expect(checkbox('access').checked).toBe(true);
    expect(checkbox('access').indeterminate).toBe(false);

    click(checkbox('access.roles'));
    expect([...screen.selected()]).toEqual(['access.grants.view']);
    expect([...screen.changes[1].removed].sort()).toEqual(['access.roles.manage', 'access.roles.view']);
  });

  it('expands and collapses one branch or all of them', () => {
    const group = item('access').querySelector(':scope > .tree-editor__group') as HTMLElement;
    expect(toggleButton('access').getAttribute('aria-expanded')).toBe('true');
    expect(toggleButton('access').getAttribute('aria-controls')).toBe(group.id);
    expect(toggleButton('access').getAttribute('aria-label')).toBe('patterns.tree.collapse|label=permissions.access');
    toggleButton('access').click();
    fixture.detectChanges();
    expect(group.hidden).toBe(true);
    expect(toggleButton('access').getAttribute('aria-expanded')).toBe('false');

    toolbarButton(0).click();
    fixture.detectChanges();
    expect(group.hidden).toBe(false);
    toolbarButton(1).click();
    fixture.detectChanges();
    expect(group.hidden).toBe(true);
    expect((item('users').querySelector('.tree-editor__group') as HTMLElement).hidden).toBe(true);
  });

  it('filters by label or key, keeping ancestors and expanding the matches', () => {
    toolbarButton(1).click();
    fixture.detectChanges();
    filter('grants');
    expect(item('access')).not.toBeNull();
    expect(item('access.grants.view')).not.toBeNull();
    expect(item('access.roles')).toBeNull();
    expect(item('users')).toBeNull();
    expect((item('access').querySelector('.tree-editor__group') as HTMLElement).hidden).toBe(false);
    filter('nothing-like-this');
    expect(textOf(host, '.tree-editor__state')).toBe('patterns.tree.no-match');
  });

  it('selects and clears only the visible keys', () => {
    filter('roles');
    toolbarButton(2).click();
    fixture.detectChanges();
    expect([...screen.selected()].sort()).toEqual(['access.roles.manage', 'access.roles.view']);
    filter('');
    toolbarButton(3).click();
    fixture.detectChanges();
    expect(screen.selected().size).toBe(0);
  });

  it('is read-only on request: disabled boxes, no select/clear, no changes', () => {
    screen.readOnly.set(true);
    fixture.detectChanges();
    expect(checkbox('access').disabled).toBe(true);
    expect(host.querySelectorAll('.tree-editor__actions dga-button')).toHaveLength(2);
    expect(screen.changes).toHaveLength(0);
  });

  it('accepts explicit nodes instead of keys', () => {
    screen.nodes.set([{ key: 'reports', label: { text: 'Reports' }, selectable: true, children: [] }]);
    fixture.detectChanges();
    expect(item('access')).toBeNull();
    expect(textOf(item('reports'), '.dga-checkbox__label')).toBe('Reports');
    expect(textOf(host, '.tree-editor__count')).toBe('patterns.tree.selected-count|selected=0,total=1');
  });

  it('shows the empty state without keys', () => {
    screen.nodes.set([]);
    fixture.detectChanges();
    expect(textOf(host, '.tree-editor__state')).toBe('patterns.tree.empty');
  });
});
