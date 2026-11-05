import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { parseApiError, type ApiError } from '@codelab/ng-security';
import { beforeEach, describe, expect, it } from 'vitest';
import { TranslationService } from '../../core/i18n/translation.service';
import { APP_CLOCK } from '../../core/session/clock';
import { ListCellDirective, ListRowActionsDirective } from '../admin-list/admin-list-templates.directive';
import type { ColumnDef, SortState } from '../admin-list/list-columns';
import { echoTranslate, textOf } from '../patterns.testing';
import { NO_SELECTION, type DueColumnDef, type QueueRowId, type QueueTab } from './queue-state';
import { QUEUE_SELECT_COLUMN_KEY, WorkQueuePatternComponent } from './work-queue-pattern.component';
import { WorkQueueTranslationKeys } from './work-queue-translation-keys';

interface TaskRow {
  readonly id: string;
  readonly reference: string;
  readonly statusKey: string;
  readonly dueAt: string | null;
}

const NOW = Date.parse('2026-03-10T12:00:00Z');
const TASKS: readonly TaskRow[] = [
  { id: 't-1', reference: 'R-1', statusKey: 'tasks.status.open', dueAt: '2026-03-09T12:00:00Z' },
  { id: 't-2', reference: 'R-2', statusKey: 'tasks.status.open', dueAt: '2026-03-10T20:00:00Z' },
  { id: 't-3', reference: 'R-3', statusKey: 'tasks.status.waiting', dueAt: null },
];

/** Living documentation: a staff inbox built on the work-queue pattern. */
@Component({
  imports: [WorkQueuePatternComponent, ListCellDirective, ListRowActionsDirective],
  template: `
    <div [attr.dir]="dir()">
      <app-work-queue-pattern
        titleKey="tasks.title"
        [tabs]="tabs()"
        [columns]="columns"
        [dueColumn]="dueColumn"
        [rows]="rows()"
        [rowId]="rowId"
        [rowLabel]="rowLabel"
        [total]="rows().length"
        [loading]="false"
        [error]="error()"
        [selectable]="selectable()"
        [(tab)]="tab"
        [(page)]="page"
        [(search)]="search"
        [(sort)]="sort"
        [(selected)]="selected"
      >
        <span queueToolbar class="toolbar-slot"></span>
        <span queueFilters class="status-filter"></span>
        <button queueBulkActions type="button" class="bulk-assign" data-public-action>assign</button>
        <ng-template listCell="statusKey" let-row><span class="status">{{ row.statusKey }}</span></ng-template>
        <ng-template listRowActions let-row>
          <button type="button" class="open" data-public-action (click)="opened = row.id">open</button>
        </ng-template>
      </app-work-queue-pattern>
    </div>
  `,
})
class InboxHost {
  readonly dir = signal<'rtl' | 'ltr'>('ltr');
  readonly tabs = signal<readonly QueueTab[]>([
    { value: 'mine', labelKey: 'tasks.tabs.mine', count: 3 },
    { value: 'team', labelKey: 'tasks.tabs.team' },
    { value: 'all', labelKey: 'tasks.tabs.all' },
  ]);
  readonly columns: readonly ColumnDef<TaskRow>[] = [
    { key: 'reference', headerKey: 'tasks.columns.reference', sortable: true },
    { key: 'statusKey', headerKey: 'tasks.columns.status' },
  ];
  readonly dueColumn: DueColumnDef<TaskRow> = { key: 'dueAt', headerKey: 'tasks.columns.due', dueAt: (row) => row.dueAt, sortable: true };
  readonly rowId = (row: TaskRow) => row.id;
  readonly rowLabel = (row: TaskRow) => row.reference;
  readonly rows = signal<readonly TaskRow[]>(TASKS);
  readonly error = signal<ApiError | null>(null);
  readonly selectable = signal(true);
  readonly tab = signal('mine');
  readonly page = signal(2);
  readonly search = signal('');
  readonly sort = signal<SortState | null>(null);
  readonly selected = signal<ReadonlySet<QueueRowId>>(NO_SELECTION);
  opened: string | null = null;
}

describe('WorkQueuePatternComponent', () => {
  let fixture: ComponentFixture<InboxHost>;
  let host: HTMLElement;
  let screen: InboxHost;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [InboxHost],
      providers: [
        { provide: TranslationService, useValue: { translate: echoTranslate, language: signal('en') } },
        { provide: APP_CLOCK, useValue: () => NOW },
      ],
    });
    fixture = TestBed.createComponent(InboxHost);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    screen = fixture.componentInstance;
  });

  const render = () => fixture.detectChanges();
  const rows = () => [...host.querySelectorAll('tbody tr.admin-list__row')];
  const tabButtons = () => [...host.querySelectorAll<HTMLButtonElement>('[role="tab"]')];
  const rowBoxes = () => [...host.querySelectorAll<HTMLInputElement>('input.work-queue__select')];

  it('renders the admin list with the slots, the select column, the screen columns and the due column', () => {
    expect(textOf(host, 'h1')).toBe('tasks.title');
    expect(host.querySelector('.admin-list__toolbar .toolbar-slot')).not.toBeNull();
    expect(host.querySelector('.admin-list__filters .status-filter')).not.toBeNull();
    const headers = [...host.querySelectorAll('thead th')].map((th) => th.textContent?.trim());
    expect(headers).toEqual([
      WorkQueueTranslationKeys.SelectColumn,
      'tasks.columns.reference',
      'tasks.columns.status',
      'tasks.columns.due',
      'patterns.list.actions',
    ]);
    expect(rows()).toHaveLength(3);
    expect(textOf(rows()[0], '.status')).toBe('tasks.status.open');
    expect(QUEUE_SELECT_COLUMN_KEY).toBe('queue-select');
  });

  it('shows the screen tabs with counts and the selected tab', () => {
    const labels = tabButtons().map((tab) => tab.textContent?.trim());
    expect(labels[0]).toBe(`${WorkQueueTranslationKeys.TabCount}|label=tasks.tabs.mine,count=3`);
    expect(labels[1]).toBe('tasks.tabs.team');
    expect(tabButtons()[0].getAttribute('aria-selected')).toBe('true');
    expect(host.querySelector('[role="tablist"]')?.getAttribute('aria-label')).toBe(WorkQueueTranslationKeys.Tabs);
  });

  it('switches tab on click, resetting the page and the selection', () => {
    screen.selected.set(new Set(['t-1']));
    render();
    tabButtons()[1].click();
    render();
    expect(screen.tab()).toBe('team');
    expect(screen.page()).toBe(1);
    expect(screen.selected().size).toBe(0);
  });

  it('moves between tabs with the arrow keys in LTR and mirrors them in RTL', () => {
    tabButtons()[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    render();
    expect(screen.tab()).toBe('team');

    screen.dir.set('rtl');
    render();
    tabButtons()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    render();
    expect(screen.tab()).toBe('all');
    expect(host.querySelector('.work-queue__due')).not.toBeNull();
  });

  it('hides the tab strip when the screen offers no tabs', () => {
    screen.tabs.set([]);
    render();
    expect(host.querySelector('[role="tablist"]')).toBeNull();
  });

  it('shows the due time with its state, and a placeholder without one', () => {
    const due = (index: number) => rows()[index].querySelector('.work-queue__due') as HTMLElement;
    expect(due(0).querySelector('time')?.getAttribute('datetime')).toBe('2026-03-09T12:00:00Z');
    expect(textOf(due(0), 'dga-status-tag')).toBe(WorkQueueTranslationKeys.Overdue);
    expect(textOf(due(1), 'dga-status-tag')).toBe(WorkQueueTranslationKeys.DueSoon);
    expect(textOf(due(2), '.work-queue__due-text')).toBe(WorkQueueTranslationKeys.NoDueDate);
  });

  it('sorts by the due column like any sortable column', () => {
    const sortButtons = [...host.querySelectorAll<HTMLButtonElement>('thead button')];
    sortButtons[sortButtons.length - 1].click();
    render();
    expect(screen.sort()?.key).toBe('dueAt');
  });

  it('selects rows with named checkboxes and shows the bulk actions while any is selected', () => {
    expect(rowBoxes()[0].getAttribute('aria-label')).toBe(`${WorkQueueTranslationKeys.SelectRow}|label=R-1`);
    expect(host.querySelector('.bulk-assign')).toBeNull();

    rowBoxes()[0].click();
    render();
    expect([...screen.selected()]).toEqual(['t-1']);
    expect(textOf(host, '.work-queue__selected-count')).toBe(`${WorkQueueTranslationKeys.SelectedCount}|count=1`);
    expect(host.querySelector('.work-queue__bulk-actions .bulk-assign')).not.toBeNull();
    expect(rowBoxes()[0].checked).toBe(true);

    rowBoxes()[0].click();
    render();
    expect(screen.selected().size).toBe(0);
  });

  it('selects and clears the whole page, and clears with the clear button', () => {
    const pageBox = () => host.querySelector('.work-queue__select-page input') as HTMLInputElement;
    pageBox().click();
    render();
    expect([...screen.selected()]).toEqual(['t-1', 't-2', 't-3']);

    (host.querySelector('.work-queue__clear button') as HTMLButtonElement).click();
    render();
    expect(screen.selected().size).toBe(0);
  });

  it('shows the page box as mixed when some rows are selected', () => {
    screen.selected.set(new Set(['t-2']));
    render();
    const pageBox = host.querySelector('.work-queue__select-page input') as HTMLInputElement;
    expect(pageBox.indeterminate).toBe(true);
  });

  it('has no select column or page box when the queue is not selectable', () => {
    screen.selectable.set(false);
    render();
    expect(rowBoxes()).toHaveLength(0);
    expect(host.querySelector('.work-queue__select-page')).toBeNull();
    expect(host.querySelector('thead th')?.textContent?.trim()).toBe('tasks.columns.reference');
  });

  it('forwards the row actions template', () => {
    (rows()[1].querySelector('.open') as HTMLButtonElement).click();
    expect(screen.opened).toBe('t-2');
  });

  it('shows the admin list error state', () => {
    screen.error.set(parseApiError({ status: 500, error: { code: 'INTERNAL_ERROR' } }));
    render();
    expect(host.querySelector('[role="alert"]')).not.toBeNull();
  });
});
