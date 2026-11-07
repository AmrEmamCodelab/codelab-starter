import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ApiError } from '@codelab/ng-security';
import { beforeEach, describe, expect, it } from 'vitest';
import { CommonKeys } from '../../core/i18n/common-translation-keys';
import { opsTestProviders, problem, textOf } from '../../core/testing/ops.testing';
import { cellText, cellValue, type ColumnDef } from './list-columns';
import { OpsListCellDirective } from './ops-list-cell.directive';
import { OpsListComponent } from './ops-list.component';

interface Row {
  readonly id: string;
  readonly name: string;
  readonly count: number | null;
}

/** Living documentation: how a screen places the ops list. */
@Component({
  imports: [OpsListComponent, OpsListCellDirective],
  template: `
    <app-ops-list
      captionKey="caption.key"
      emptyKey="empty.key"
      [columns]="columns"
      [rows]="rows()"
      [rowId]="rowId"
      [loading]="loading()"
      [error]="error()"
      (retry)="retried = retried + 1"
    >
      <ng-template opsListCell="count" let-row let-value="value"><b class="count">#{{ value }}</b></ng-template>
    </app-ops-list>
  `,
})
class Host {
  readonly columns: readonly ColumnDef<Row>[] = [
    { key: 'name', headerKey: 'col.name', ltr: true },
    { key: 'upper', headerKey: 'col.upper', value: (row) => row.name.toUpperCase() },
    { key: 'count', headerKey: 'col.count' },
  ];
  readonly rows = signal<readonly Row[]>([]);
  readonly loading = signal(false);
  readonly error = signal<ApiError | null>(null);
  readonly rowId = (row: Row) => row.id;
  retried = 0;
}

describe('list column helpers', () => {
  it('reads the accessor, else the property', () => {
    const row: Row = { id: '1', name: 'x', count: 2 };
    expect(cellValue({ key: 'name', headerKey: 'h' }, row)).toBe('x');
    expect(cellValue({ key: 'n', headerKey: 'h', value: () => 5 }, row)).toBe(5);
    expect(cellValue({ key: 'n', headerKey: 'h' }, 'primitive')).toBeUndefined();
    expect(cellText(null)).toBe('');
    expect(cellText(3)).toBe('3');
  });
});

describe('OpsListComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: opsTestProviders() }));

  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return { fixture, host: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
  }

  it('renders the caption, translated headers, plain, accessor and template cells', () => {
    const { fixture, host, root } = render();
    host.rows.set([{ id: '1', name: 'alpha', count: 4 }]);
    fixture.detectChanges();
    expect(textOf(root, 'caption')).toBe('caption.key');
    expect([...root.querySelectorAll('th')].map((th) => th.textContent?.trim())).toEqual(['col.name', 'col.upper', 'col.count']);
    const cells = [...root.querySelectorAll('td')];
    expect(cells[0]?.textContent?.trim()).toBe('alpha');
    expect(cells[0]?.getAttribute('dir')).toBe('ltr');
    expect(cells[1]?.textContent?.trim()).toBe('ALPHA');
    expect(textOf(root, '.count')).toBe('#4');
  });

  it('shows the loading row only while there are no rows', () => {
    const { fixture, host, root } = render();
    host.loading.set(true);
    fixture.detectChanges();
    expect(textOf(root, '[role="status"]')).toBe(CommonKeys.List.Loading);
    expect(root.querySelector('table')?.getAttribute('aria-busy')).toBe('true');
  });

  it('shows the empty message when loaded with no rows', () => {
    const { root } = render();
    expect(root.textContent).toContain('empty.key');
  });

  it('shows the error with its reference code and forwards retry', () => {
    const { fixture, host, root } = render();
    host.error.set(problem(502, 'UPSTREAM_UNAVAILABLE'));
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('errors.UPSTREAM_UNAVAILABLE');
    expect(textOf(root, '[data-testid="error-reference-code"]')).toBe(`${CommonKeys.ReferenceCode}|code=7D2C1-E9A04`);
    expect(root.textContent).not.toContain('empty.key');
    root.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    expect(host.retried).toBe(1);
  });

  it('falls back to the generic message for a code it has no text for', () => {
    const { fixture, host, root } = render();
    host.error.set(problem(502, 'GATEWAY_UPSTREAM_REJECTED'));
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('errors.INTERNAL_ERROR');
  });
});
