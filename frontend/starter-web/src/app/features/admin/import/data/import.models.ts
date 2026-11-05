import type { components } from '../../../../core/api/generated/openapi';

type Schemas = components['schemas'];

/** A previewed import batch's PublicId (label `user-imports`). */
export type ImportBatchId = Schemas['PublicIdOfUserImportIds'];
export type ImportPreview = Schemas['ImportPreviewResponse'];
export type ImportPreviewRow = Schemas['ImportPreviewRowResponse'];
export type ImportCommit = Schemas['ImportCommitResponse'];
export type ImportedUser = Schemas['ImportedUserResponse'];
export type RejectedImportRow = Schemas['RejectedImportRowResponse'];
