import { AppLanguages, type AppLanguage } from '../../../../core/i18n/language';

/**
 * A lookup value's id: the integer the migration seeds (the backend `<Name>Ids` constants). Lookup ids are the one
 * id on the wire that is not a PublicId: they are reference data every client may name by constant.
 */
export type LookupItemId = number;

/** One lookup type, as `GET api/admin/lookups` lists it. */
export interface LookupType {
  /** The type key (`contact-topics`): the route parameter of its values page. */
  readonly type: string;
  readonly nameAr: string;
  readonly nameEn: string;
  readonly itemCount: number;
}

/** One lookup value, as `GET api/admin/lookups/{type}` lists it (active and inactive). */
export interface AdminLookupItem {
  readonly id: LookupItemId;
  readonly code: string;
  readonly nameAr: string;
  readonly nameEn: string;
  readonly sortOrder: number;
  readonly isActive: boolean;
}

/** The body of `PUT api/admin/lookups/{type}/items/{id}/active`. */
export interface SetLookupItemActiveBody {
  readonly isActive: boolean;
}

/** Anything with a bilingual name (a type or a value). */
export interface BilingualName {
  readonly nameAr: string;
  readonly nameEn: string;
}

/** The name in `language` (Arabic unless English is asked for). */
export function localizedName(named: BilingualName, language: AppLanguage): string {
  return language === AppLanguages.English ? named.nameEn : named.nameAr;
}
