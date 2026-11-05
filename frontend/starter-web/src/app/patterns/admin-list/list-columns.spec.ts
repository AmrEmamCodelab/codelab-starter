import { describe, expect, it } from 'vitest';
import { SortDirection, ariaSort, cellText, cellValue, nextSort, type ColumnDef } from './list-columns';

interface Row {
  readonly userName: string;
  readonly email: string | null;
}

const ROW: Row = { userName: 'sara', email: null };

describe('list columns', () => {
  it('reads the row property named by the key, or the accessor', () => {
    expect(cellValue<Row>({ key: 'userName', headerKey: 'h' }, ROW)).toBe('sara');
    expect(cellValue<Row>({ key: 'name', headerKey: 'h', value: (row) => row.userName.toUpperCase() }, ROW)).toBe('SARA');
    expect(cellValue<Row>({ key: 'missing', headerKey: 'h' }, ROW)).toBeUndefined();
    expect(cellValue<number>({ key: 'x', headerKey: 'h' }, 4)).toBeUndefined();
  });

  it('renders null and undefined as empty text', () => {
    expect(cellText(null)).toBe('');
    expect(cellText(undefined)).toBe('');
    expect(cellText(0)).toBe('0');
    expect(cellText(false)).toBe('false');
  });

  it('starts a new column ascending and flips the same column', () => {
    const first = nextSort(null, 'userName');
    expect(first).toEqual({ key: 'userName', direction: SortDirection.Ascending });
    const flipped = nextSort(first, 'userName');
    expect(flipped.direction).toBe(SortDirection.Descending);
    expect(nextSort(flipped, 'userName').direction).toBe(SortDirection.Ascending);
    expect(nextSort(flipped, 'email')).toEqual({ key: 'email', direction: SortDirection.Ascending });
  });

  it('maps the sort to aria-sort', () => {
    const sortable: ColumnDef<Row> = { key: 'userName', headerKey: 'h', sortable: true };
    const plain: ColumnDef<Row> = { key: 'email', headerKey: 'h' };
    expect(ariaSort(null, plain)).toBeNull();
    expect(ariaSort(null, sortable)).toBe('none');
    expect(ariaSort({ key: 'userName', direction: SortDirection.Ascending }, sortable)).toBe('ascending');
    expect(ariaSort({ key: 'userName', direction: SortDirection.Descending }, sortable)).toBe('descending');
    expect(ariaSort({ key: 'other', direction: SortDirection.Descending }, sortable)).toBe('none');
  });

  it('keeps enum values explicit integers', () => {
    expect(SortDirection.Ascending).toBe(1);
    expect(SortDirection.Descending).toBe(2);
  });
});
