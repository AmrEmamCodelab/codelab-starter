import { lastSegmentLabel, type TreeLabel, type TreeLabelResolver } from '../../../../patterns';
import { AppLanguages, type AppLanguage } from '../../../../core/i18n/language';
import {
  AccessUserKind,
  PermissionScope,
  PermissionSourceKind,
  VirtualRoleKind,
  type CatalogPermission,
} from '../data/access.models';

/** A bilingual record (roles carry both names). */
export interface BilingualName {
  readonly nameEn: string;
  readonly nameAr: string;
}

/** The record's name in `language`, falling back to the other language when one is blank. */
export function localizedName(named: BilingualName, language: AppLanguage): string {
  const preferred = language === AppLanguages.Arabic ? named.nameAr : named.nameEn;
  const other = language === AppLanguages.Arabic ? named.nameEn : named.nameAr;
  return preferred.trim() !== '' ? preferred : other;
}

/** The bundle prefix of a permission key's label: `permissions.<key>` (flat keys, so dotted keys never collide). */
export const PERMISSION_LABEL_PREFIX = 'permissions.';
/** The bundle prefix of a permission group's label: a tree branch such as `access` or `access.roles`. */
export const PERMISSION_GROUP_LABEL_PREFIX = 'admin.access.permission-groups.';

export function permissionLabelKey(key: string): string {
  return `${PERMISSION_LABEL_PREFIX}${key}`;
}

export function permissionGroupLabelKey(key: string): string {
  return `${PERMISSION_GROUP_LABEL_PREFIX}${key}`;
}

/**
 * The tree label of a catalog key or a group: its translation when the bundle has one, else the catalog's English
 * description for a key, else the last key segment for a group. `hasText` is `TranslationService.has`, so the
 * resolver follows a language switch when it is rebuilt inside a `computed`.
 */
export function permissionLabelResolver(
  catalog: ReadonlyMap<string, CatalogPermission>,
  hasText: (translationKey: string) => boolean,
): TreeLabelResolver {
  return (key: string): TreeLabel => {
    const entry = catalog.get(key);
    const labelKey = entry ? permissionLabelKey(key) : permissionGroupLabelKey(key);
    if (hasText(labelKey)) {
      return { textKey: labelKey };
    }
    return entry ? { text: entry.description } : lastSegmentLabel(key);
  };
}

const SCOPE_LABEL_KEYS: Readonly<Record<PermissionScope, string>> = {
  [PermissionScope.Global]: 'admin.access.scope.global',
  [PermissionScope.OwnOrganization]: 'admin.access.scope.own-organization',
};

export function scopeLabelKey(scope: PermissionScope): string {
  return SCOPE_LABEL_KEYS[scope];
}

const VIRTUAL_KIND_LABEL_KEYS: Readonly<Record<VirtualRoleKind, string>> = {
  [VirtualRoleKind.AllInternalUsers]: 'admin.access.virtual-kind.all-internal-users',
  [VirtualRoleKind.AllExternalUsers]: 'admin.access.virtual-kind.all-external-users',
  [VirtualRoleKind.AllAuthenticatedUsers]: 'admin.access.virtual-kind.all-authenticated-users',
};

/** The audience of a virtual role; an unknown kind reads as every signed-in user. */
export function virtualKindLabelKey(kind: number | null): string {
  return VIRTUAL_KIND_LABEL_KEYS[kind as VirtualRoleKind] ?? VIRTUAL_KIND_LABEL_KEYS[VirtualRoleKind.AllAuthenticatedUsers];
}

const USER_KIND_LABEL_KEYS: Readonly<Record<AccessUserKind, string>> = {
  [AccessUserKind.InternalUser]: 'admin.access.grants.kind-internal',
  [AccessUserKind.ExternalUser]: 'admin.access.grants.kind-external',
};

export function userKindLabelKey(kind: number): string {
  return USER_KIND_LABEL_KEYS[kind as AccessUserKind] ?? USER_KIND_LABEL_KEYS[AccessUserKind.InternalUser];
}

const SOURCE_LABEL_KEYS: Readonly<Record<PermissionSourceKind, string>> = {
  [PermissionSourceKind.Direct]: 'admin.access.grants.source.direct',
  [PermissionSourceKind.Role]: 'admin.access.grants.source.role',
  [PermissionSourceKind.VirtualRole]: 'admin.access.grants.source.virtual-role',
  [PermissionSourceKind.BreakGlass]: 'admin.access.grants.source.break-glass',
};

export function sourceLabelKey(kind: number): string {
  return SOURCE_LABEL_KEYS[kind as PermissionSourceKind] ?? SOURCE_LABEL_KEYS[PermissionSourceKind.Direct];
}

/** The display text of a tree label: its translation, or the text as is. */
export function labelText(label: TreeLabel, translate: (translationKey: string) => string): string {
  return 'textKey' in label ? translate(label.textKey) : label.text;
}
