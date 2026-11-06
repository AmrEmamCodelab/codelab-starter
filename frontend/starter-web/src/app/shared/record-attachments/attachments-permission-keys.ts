import { PermissionKeys } from '../../core/permissions/permission-keys.generated';

/** The Attachments module's permission keys, from the generated catalogue (`npm run api:gen`). */
export const AttachmentsPermissionKeys = {
  /** Types, the record's list and downloads. */
  View: PermissionKeys.AttachmentsView,
  /** Upload tickets and links. */
  Upload: PermissionKeys.AttachmentsUpload,
  /** Remove a linked attachment. */
  Remove: PermissionKeys.AttachmentsRemove,
} as const;
