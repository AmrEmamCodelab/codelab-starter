import { describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { serviceRequestCardsAdapter } from './service-request-cards';
import { serviceRequestDetails as details } from './service-requests.testing';

describe('serviceRequestCardsAdapter', () => {
  it('builds the request and details cards with every field', () => {
    expect(serviceRequestCardsAdapter(details(), AppLanguages.English)).toEqual([
      {
        titleKey: 'serviceRequests.cards.request',
        fields: [
          { labelKey: 'serviceRequests.fields.categoryId', value: 'Certificates' },
          { labelKey: 'serviceRequests.fields.title', value: 'Experience certificate' },
        ],
      },
      {
        titleKey: 'serviceRequests.cards.details',
        fields: [
          { labelKey: 'serviceRequests.fields.description', value: 'Please issue an experience certificate.' },
          { labelKey: 'serviceRequests.fields.contactPhone', value: '\u0030512345678' },
          { labelKey: 'serviceRequests.fields.preferredDate', value: '2026-11-20' },
        ],
      },
    ]);
  });

  it('shows the Arabic category name by default', () => {
    expect(serviceRequestCardsAdapter(details())[0].fields[0].value).toBe('شهادات');
    expect(serviceRequestCardsAdapter(details(), AppLanguages.Arabic)[0].fields[0].value).toBe('شهادات');
  });

  it('leaves out the preferred date row when none was given', () => {
    const labels = serviceRequestCardsAdapter(details({ preferredDate: null }))[1].fields.map((field) => field.labelKey);

    expect(labels).toEqual(['serviceRequests.fields.description', 'serviceRequests.fields.contactPhone']);
  });
});
