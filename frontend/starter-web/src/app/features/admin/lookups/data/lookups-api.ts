import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../../core/api/api.service';
import { API_PREFIX } from '../../../../core/api/clients/api-paths';
import { quietContext } from '../../../../core/http/http-context';
import type { AdminLookupItem, LookupItemId, LookupType, SetLookupItemActiveBody } from './lookup.models';

const LOOKUPS_ROOT = `${API_PREFIX}/admin/lookups`;

function typePath(type: string): string {
  return `${LOOKUPS_ROOT}/${encodeURIComponent(type)}`;
}

/** The lookup administration endpoints, relative to the API root (no leading slash). */
export const LookupApiPaths = {
  Types: LOOKUPS_ROOT,
  items: typePath,
  itemActive: (type: string, id: LookupItemId): string =>
    `${typePath(type)}/items/${encodeURIComponent(String(id))}/active`,
} as const;

/**
 * The lookup administration client (`lookups.view` / `lookups.manage`), a thin wrapper over `ApiService`. Every call
 * is quiet (no error dialog): the lists show their own error state with the reference code and the confirmation
 * dialog shows a refused write inline. A 401 still ends the session.
 */
@Injectable({ providedIn: 'root' })
export class LookupsAdminClient {
  private readonly api = inject(ApiService);

  /** Every lookup type with its value count. */
  types(): Observable<readonly LookupType[]> {
    return this.api.get<readonly LookupType[]>(LookupApiPaths.Types, { context: quietContext() });
  }

  /** Every value of `type`, active and inactive; an unknown type answers 404. */
  items(type: string): Observable<readonly AdminLookupItem[]> {
    return this.api.get<readonly AdminLookupItem[]>(LookupApiPaths.items(type), { context: quietContext() });
  }

  /** Activates or deactivates one value (204). */
  setActive(type: string, id: LookupItemId, isActive: boolean): Observable<void> {
    const body: SetLookupItemActiveBody = { isActive };
    return this.api.put<void>(LookupApiPaths.itemActive(type, id), body, { context: quietContext() });
  }
}
