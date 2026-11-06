import { AppLanguages, DEFAULT_LANGUAGE, type AppLanguage } from '../../../core/i18n/language';
import type { ServiceRequestDetails } from './service-request.models';
import { ServiceRequestTranslationKeys } from './service-requests-translation-keys';

/*
 * The record-type adapter of the sample: it turns `ServiceRequestDetails` into the generic detail cards that
 * shared/records `RecordDetailCardsComponent` renders (`[cards]`). These two interfaces are structurally the
 * shared/records `RecordCard` contract ({ titleKey, fields: { labelKey, value }[] }); once shared/records exists, they
 * may be replaced by its exports without changing the adapter.
 */

/** One labelled value of a detail card. `value` is display text (already in the user's language). */
export interface ServiceRequestCardField {
  readonly labelKey: string;
  readonly value: string;
}

/** One detail card: a titled group of fields. */
export interface ServiceRequestCard {
  readonly titleKey: string;
  readonly fields: readonly ServiceRequestCardField[];
}

/**
 * The detail cards of a service request: "request" (category, title) and "details" (description, contact phone,
 * preferred date). The category shows in `language` (Arabic by default, the app's default language); a request
 * without a preferred date has no preferred-date row.
 */
export function serviceRequestCardsAdapter(
  details: ServiceRequestDetails,
  language: AppLanguage = DEFAULT_LANGUAGE,
): readonly ServiceRequestCard[] {
  const category = language === AppLanguages.English ? details.categoryNameEn : details.categoryNameAr;
  const detailFields: ServiceRequestCardField[] = [
    { labelKey: ServiceRequestTranslationKeys.FieldDescription, value: details.description },
    { labelKey: ServiceRequestTranslationKeys.FieldContactPhone, value: details.contactPhone },
  ];
  if (details.preferredDate !== null) {
    detailFields.push({ labelKey: ServiceRequestTranslationKeys.FieldPreferredDate, value: details.preferredDate });
  }
  return [
    {
      titleKey: ServiceRequestTranslationKeys.CardRequest,
      fields: [
        { labelKey: ServiceRequestTranslationKeys.FieldCategory, value: category },
        { labelKey: ServiceRequestTranslationKeys.FieldTitle, value: details.title },
      ],
    },
    { titleKey: ServiceRequestTranslationKeys.CardDetails, fields: detailFields },
  ];
}
