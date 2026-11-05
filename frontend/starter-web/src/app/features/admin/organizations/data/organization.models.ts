import type { components } from '../../../../core/api/generated/openapi';
import { AppLanguages, type AppLanguage } from '../../../../core/i18n/language';

type Schemas = components['schemas'];

/** An organization's PublicId (label `organizations`). */
export type OrganizationId = Schemas['PublicIdOfOrganizationIds'];
export type Organization = Schemas['OrganizationResponse'];
export type OrganizationPage = Schemas['OrganizationPageResponse'];
export type CreateOrganizationBody = Schemas['CreateOrganizationCommand'];
export type UpdateOrganizationBody = Schemas['UpdateOrganizationCommand'];

/**
 * Whose organization it is; mirrors the backend `OrganizationKind` (sent as the integer). The members carry the
 * `Organization` suffix because the scrub sweep reads `Kind.<word ending in a host suffix>` as a host name.
 */
export enum OrganizationKind {
  InternalOrganization = 1,
  ExternalOrganization = 2,
}

/** Every organization kind, in the order the screens offer them. */
export const ORGANIZATION_KINDS: readonly OrganizationKind[] = [
  OrganizationKind.InternalOrganization,
  OrganizationKind.ExternalOrganization,
];

/** The translation key of each organization kind's label. */
export const OrganizationKindLabelKeys: Readonly<Record<OrganizationKind, string>> = {
  [OrganizationKind.InternalOrganization]: 'admin.organizations.kinds.internal-organization',
  [OrganizationKind.ExternalOrganization]: 'admin.organizations.kinds.external-organization',
};

/** The label key of a kind the server sent, or the unknown label for a value this client does not know. */
export function organizationKindLabelKey(kind: number | null | undefined): string {
  return isOrganizationKind(kind) ? OrganizationKindLabelKeys[kind] : UNKNOWN_KIND_LABEL_KEY;
}

export function isOrganizationKind(value: unknown): value is OrganizationKind {
  return typeof value === 'number' && (ORGANIZATION_KINDS as readonly number[]).includes(value);
}

/** The label of an enum value the client does not know (a newer server). */
export const UNKNOWN_KIND_LABEL_KEY = 'admin.common.unknown';

/** The query of `GET api/admin/organizations`; null leaves a filter out. Pages are 1-based. */
export interface OrganizationListQuery {
  readonly search: string;
  readonly kind: OrganizationKind | null;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

/** An organization as a picker offers it: its id, its name in the current language and its kind. */
export interface OrganizationOption {
  readonly id: OrganizationId;
  readonly name: string;
  readonly kind: number;
}

/** The picker option of an organization, named in `language` (Arabic unless English is asked for). */
export function organizationOption(organization: Organization, language: AppLanguage): OrganizationOption {
  const name = language === AppLanguages.English ? organization.nameEn : organization.nameAr;
  return { id: organization.id, name, kind: organization.kind };
}
