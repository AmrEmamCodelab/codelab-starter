import { HttpParams } from '@angular/common/http';
import { RecordsQueryParams } from './records-api';
import type { MineQuery, OversightQuery, PageRequest, QueueQuery } from './records.models';

/** A query value as a screen holds it: unset values (`null`, `undefined`, blank text) are left out of the URL. */
type QueryValue = string | number | null | undefined;

/**
 * The query string of a list request: one parameter per set value, in the given order. Blank text is left out, so a
 * cleared filter never sends `number=`; numbers are sent as they are (a 0 is a value).
 */
export function queryParams(entries: readonly (readonly [string, QueryValue])[]): HttpParams {
  let params = new HttpParams();
  for (const [name, value] of entries) {
    if (value === null || value === undefined) {
      continue;
    }
    const text = typeof value === 'number' ? String(value) : value.trim();
    if (text.length > 0) {
      params = params.set(name, text);
    }
  }
  return params;
}

function pageEntries(query: PageRequest): [string, QueryValue][] {
  return [
    [RecordsQueryParams.Page, query.page],
    [RecordsQueryParams.PageSize, query.pageSize],
  ];
}

/** `?status=&page=&pageSize=` of the caller's own records. */
export function mineParams(query: MineQuery = {}): HttpParams {
  return queryParams([[RecordsQueryParams.Status, query.status], ...pageEntries(query)]);
}

/** `?status=&number=&page=&pageSize=` of the admin oversight list. */
export function oversightParams(query: OversightQuery = {}): HttpParams {
  return queryParams([
    [RecordsQueryParams.Status, query.status],
    [RecordsQueryParams.Number, query.number],
    ...pageEntries(query),
  ]);
}

/** `?view=&status=&number=&submittedFrom=&submittedTo=&page=&pageSize=` of a work queue. */
export function queueParams(query: QueueQuery): HttpParams {
  return queryParams([
    [RecordsQueryParams.View, query.view],
    [RecordsQueryParams.Status, query.status],
    [RecordsQueryParams.Number, query.number],
    [RecordsQueryParams.SubmittedFrom, query.submittedFrom],
    [RecordsQueryParams.SubmittedTo, query.submittedTo],
    ...pageEntries(query),
  ]);
}
