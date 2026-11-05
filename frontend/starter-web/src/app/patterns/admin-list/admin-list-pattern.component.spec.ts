import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { DgaButtonComponent } from '@codelab/ng-dga';
import { parseApiError, type ApiError } from '@codelab/ng-security';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { patternTestProviders, textOf } from '../patterns.testing';
import { AdminListPatternComponent, SEARCH_DEBOUNCE_MS } from './admin-list-pattern.component';
import { ListCellDirective, ListRowActionsDirective } from './admin-list-templates.directive';
import { SortDirection, type ColumnDef, type SortState } from './list-columns';

interface UserRow {
  readonly id: string;
  readonly userName: string;
  readonly displayName: string;
  readonly isActive: boolean;
}

const USERS: readonly UserRow[] = [
  { id: 'u-1', userName: 'sara', displayName: 'Sara Ali', isActive: true },
  { id: 'u-2', userName: 'omar', displayName: 'Omar Saleh', isActive: false },
];

/** Living documentation: how a screen uses the admin-list pattern. */
@Component({
  imports: [AdminListPatternComponent, ListCellDirective, ListRowActionsDirective, DgaButtonComponent],
  template: `
    <app-admin-list-pattern
      titleKey="admin.users.title"
      [columns]="columns"
      [rows]="rows()"
      [rowId]="rowId"
      [total]="total()"
      [pageSize]="pageSize"
      [loading]="loading()"
      [error]="error()"
      [translatedCodes]="translatedCodes()"
      [(page)]="page"
      [(search)]="search"
      [(sort)]="sort"
      (retry)="retried = retried + 1"
    >
      <dga-button listToolbar class="toolbar-new" data-public-action>new</dga-button>
      <span listFilters class="filter-slot"></span>
      <ng-template listCell="isActive" let-row>
        <span class="status">{{ row.isActive ? 'on' : 'off' }}</span>
      </ng-template>
      <ng-template listRowActions let-row>
        <button type="button" class="edit" data-public-action (click)="edited = row.id">edit</button>
      </ng-template>
    </app-admin-list-pattern>
  `,
})
class UsersHost {
  readonly columns: readonly ColumnDef<UserRow>[] = [
    { key: 'userName', headerKey: 'admin.users.columns.user-name', sortable: true },
    { key: 'displayName', headerKey: 'admin.users.columns.display-name', value: (row) => row.displayName.toUpperCase() },
    { key: 'isActive', headerKey: 'admin.users.columns.status' },
  ];
  readonly rowId = (row: UserRow) => row.id;
  readonly pageSize = 20;
  readonly rows = signal<readonly UserRow[]>(USERS);
  readonly total = signal(45);
  readonly loading = signal(false);
  readonly error = signal<ApiError | null>(null);
  readonly translatedCodes = signal<ReadonlySet<string> | undefined>(undefined);
  readonly page = signal(2);
  readonly search = signal('');
  readonly sort = signal<SortState | null>(null);
  retried = 0;
  edited: string | null = null;
}

describe('AdminListPatternComponent', () => {
  let fixture: ComponentFixture<UsersHost>;
  let host: HTMLElement;
  let screen: UsersHost;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [UsersHost], providers: patternTestProviders() });
    fixture = TestBed.createComponent(UsersHost);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    screen = fixture.componentInstance;
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  const bodyRows = () => [...host.querySelectorAll('tbody tr.admin-list__row')];
  const searchInput = () => host.querySelector<HTMLInputElement>('input[type="search"]') as HTMLInputElement;

  function type(text: string): void {
    const input = searchInput();
    input.value = text;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('renders the title, slots, headers and one row per item', () => {
    expect(textOf(host, 'h1')).toBe('admin.users.title');
    expect(host.querySelector('section')?.getAttribute('aria-labelledby')).toBe(host.querySelector('h1')?.id);
    expect(host.querySelector('.admin-list__toolbar .toolbar-new')).not.toBeNull();
    expect(host.querySelector('.admin-list__filters .filter-slot')).not.toBeNull();
    const headers = [...host.querySelectorAll('thead th')].map((th) => th.textContent?.trim());
    expect(headers).toEqual([
      'admin.users.columns.user-name',
      'admin.users.columns.display-name',
      'admin.users.columns.status',
      'patterns.list.actions',
    ]);
    expect(bodyRows()).toHaveLength(2);
  });

  it('fills cells from the key, the accessor and a projected cell template', () => {
    const cells = [...bodyRows()[0].querySelectorAll('td')].map((td) => td.textContent?.trim());
    expect(cells).toEqual(['sara', 'SARA ALI', 'on', 'edit']);
    expect(textOf(bodyRows()[1], '.status')).toBe('off');
  });

  it('passes the row to the row actions', () => {
    bodyRows()[1].querySelector<HTMLButtonElement>('.edit')?.click();
    expect(screen.edited).toBe('u-2');
  });

  it('debounces the search, then resets to page 1', () => {
    vi.useFakeTimers();
    type('om');
    type('omar ');
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 1);
    expect(screen.search()).toBe('');
    vi.advanceTimersByTime(1);
    expect(screen.search()).toBe('omar');
    expect(screen.page()).toBe(1);
  });

  it('applies the search at once on Enter (form submit)', () => {
    vi.useFakeTimers();
    type('sara');
    host.querySelector('form[role="search"]')?.dispatchEvent(new Event('submit', { cancelable: true }));
    expect(screen.search()).toBe('sara');
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    expect(screen.search()).toBe('sara');
  });

  it('sorts from a sortable header with aria-sort, and resets the page', () => {
    const header = host.querySelector('thead th');
    expect(header?.getAttribute('aria-sort')).toBe('none');
    expect(host.querySelectorAll('thead th')[1].getAttribute('aria-sort')).toBeNull();
    header?.querySelector<HTMLButtonElement>('button.admin-list__sort')?.click();
    fixture.detectChanges();
    expect(screen.sort()).toEqual({ key: 'userName', direction: SortDirection.Ascending });
    expect(screen.page()).toBe(1);
    expect(header?.getAttribute('aria-sort')).toBe('ascending');
    header?.querySelector<HTMLButtonElement>('button.admin-list__sort')?.click();
    fixture.detectChanges();
    expect(header?.getAttribute('aria-sort')).toBe('descending');
  });

  it('pages through the pager', () => {
    expect(host.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe('2');
    host.querySelectorAll<HTMLButtonElement>('.list-pager__page')[2].click();
    fixture.detectChanges();
    expect(screen.page()).toBe(3);
  });

  it('shows the loading state while there are no rows, and marks the table busy', () => {
    screen.rows.set([]);
    screen.loading.set(true);
    fixture.detectChanges();
    expect(host.querySelector('table')?.getAttribute('aria-busy')).toBe('true');
    expect(textOf(host, 'td[role="status"]')).toBe('patterns.list.loading');
    expect(host.querySelector('td[role="status"]')?.getAttribute('colspan')).toBe('4');
  });

  it('shows the empty state', () => {
    screen.rows.set([]);
    screen.total.set(0);
    fixture.detectChanges();
    expect(textOf(host, 'td.admin-list__state')).toBe('patterns.list.empty');
  });

  it('shows the error with its reference code and retries', () => {
    screen.error.set(parseApiError({ status: 500, error: { code: 'INTERNAL_ERROR', correlationId: '5c1e9b0d2f7a4e3c8b6d1a0f9e8d7c6b' } }));
    fixture.detectChanges();
    const alert = host.querySelector('[role="alert"]') as HTMLElement;
    expect(textOf(alert, '.admin-list__error-title')).toBe('patterns.list.error');
    expect(textOf(alert, '.admin-list__error-message')).toBe('errors.INTERNAL_ERROR');
    expect(textOf(alert, '.admin-list__error-reference')).toBe(
      `patterns.common.reference-code|code=${screen.error()?.referenceCode}`,
    );
    expect(bodyRows()).toHaveLength(0);
    expect(host.querySelector('app-list-pager')).toBeNull();
    alert.querySelector<HTMLButtonElement>('dga-button button')?.click();
    expect(screen.retried).toBe(1);
  });

  it('shows a module code as itself only when the screen lists it in translatedCodes', () => {
    screen.error.set(parseApiError({ status: 409, error: { code: 'USERS_ORGANIZATION_CODE_TAKEN' } }));
    fixture.detectChanges();
    expect(textOf(host, '.admin-list__error-message')).toBe('errors.INTERNAL_ERROR');
    screen.translatedCodes.set(new Set(['USERS_ORGANIZATION_CODE_TAKEN']));
    fixture.detectChanges();
    expect(textOf(host, '.admin-list__error-message')).toBe('errors.USERS_ORGANIZATION_CODE_TAKEN');
  });
});
