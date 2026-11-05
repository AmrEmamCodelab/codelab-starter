import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../../core/api/api.service';
import { API_PREFIX } from '../../../../core/api/clients/api-paths';
import { quietContext } from '../../../../core/http/http-context';
import type { ImportBatchId, ImportCommit, ImportPreview } from './import.models';

const IMPORT_ROOT = `${API_PREFIX}/admin/user-import`;

/** The user import endpoints (`users.import`), relative to the API root (no leading slash). */
export const UserImportApiPaths = {
  Preview: `${IMPORT_ROOT}/preview`,
  commit: (batchId: ImportBatchId): string => `${IMPORT_ROOT}/${encodeURIComponent(batchId)}/commit`,
} as const;

/** The multipart field the preview reads the workbook from (the backend `UsersRoutes.FileField`). */
export const IMPORT_FILE_FIELD = 'file';

/**
 * The Excel user import client, a thin wrapper over `ApiService`. Both calls are quiet (no error dialog): the import
 * screen shows the refusal inline with its reference code. The preview is a multipart upload; request signing leaves
 * a multipart body unsigned by design (`@codelab/ng-security`).
 */
@Injectable({ providedIn: 'root' })
export class UserImportClient {
  private readonly api = inject(ApiService);

  /** Uploads one .xlsx and answers every row with its verdict, plus the batch to commit. */
  preview(file: File): Observable<ImportPreview> {
    const body = new FormData();
    body.append(IMPORT_FILE_FIELD, file, file.name);
    return this.api.post<ImportPreview>(UserImportApiPaths.Preview, body, { context: quietContext() });
  }

  /** Creates the batch's valid rows (re-validated by the server); a batch commits once, before it expires. */
  commit(batchId: ImportBatchId): Observable<ImportCommit> {
    return this.api.post<ImportCommit>(UserImportApiPaths.commit(batchId), null, { context: quietContext() });
  }
}
