import type { AppLanguage } from '../../../core/i18n/language';
import { formatRecordInstant, type RecordCard, type RecordCardField, type RecordDetail } from '../../../shared/records';
import { serviceRequestCardsAdapter, type ServiceRequestDetails } from '../data';
import { ServiceRequestsStaffTranslationKeys } from './service-requests-staff-translation-keys';

const Keys = ServiceRequestsStaffTranslationKeys.Record;

/**
 * The record card the staff and admin details open with: the number (kept left-to-right), the requester, when it
 * was submitted and, when there is one, the latest decision reason (the reason the requester also sees).
 */
export function serviceRequestRecordCard(detail: RecordDetail, language: AppLanguage): RecordCard {
  const fields: RecordCardField[] = [
    { labelKey: Keys.Number, value: detail.number, valueDir: 'ltr' },
    { labelKey: Keys.Owner, value: detail.ownerDisplayName },
    { labelKey: Keys.SubmittedAt, value: detail.submittedAt === null ? null : formatRecordInstant(detail.submittedAt, language) },
  ];
  if (detail.lastReason !== null && detail.lastReason.trim().length > 0) {
    fields.push({ labelKey: Keys.LastReason, value: detail.lastReason, wide: true });
  }
  return { titleKey: Keys.Card, fields };
}

/** Every card of the staff and admin details: the record card, then the request's own cards (the typed adapter). */
export function serviceRequestDetailCards(
  detail: RecordDetail,
  details: ServiceRequestDetails,
  language: AppLanguage,
): readonly RecordCard[] {
  return [serviceRequestRecordCard(detail, language), ...serviceRequestCardsAdapter(details, language)];
}
