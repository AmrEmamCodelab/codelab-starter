import type { DgaGeoName } from '@codelab/ng-dga';

/**
 * The starter's static region / city / district seed: the 13 regions of Saudi Arabia, the main cities of each, and a
 * few districts of the largest cities. Enough to build and demonstrate an address form in P4. Phase 5 replaces it
 * with bilingual lookup data from the API (the data source keeps the same contract). Ids are stable slugs.
 */
export interface GeoSeedDistrict {
  readonly id: string;
  readonly name: DgaGeoName;
}

export interface GeoSeedCity extends GeoSeedDistrict {
  readonly districts: readonly GeoSeedDistrict[];
}

export interface GeoSeedRegion extends GeoSeedDistrict {
  readonly cities: readonly GeoSeedCity[];
}

export const GEO_SEED: readonly GeoSeedRegion[] = [
  {
    id: 'riyadh',
    name: { ar: 'الرياض', en: 'Riyadh' },
    cities: [
      {
        id: 'riyadh-city',
        name: { ar: 'الرياض', en: 'Riyadh' },
        districts: [
          { id: 'riyadh-al-olaya', name: { ar: 'العليا', en: 'Al Olaya' } },
          { id: 'riyadh-al-malaz', name: { ar: 'الملز', en: 'Al Malaz' } },
          { id: 'riyadh-al-nakheel', name: { ar: 'النخيل', en: 'Al Nakheel' } },
          { id: 'riyadh-al-yasmin', name: { ar: 'الياسمين', en: 'Al Yasmin' } },
        ],
      },
      { id: 'al-kharj', name: { ar: 'الخرج', en: 'Al Kharj' }, districts: [] },
      { id: 'ad-diriyah', name: { ar: 'الدرعية', en: 'Ad Diriyah' }, districts: [] },
    ],
  },
  {
    id: 'makkah',
    name: { ar: 'مكة المكرمة', en: 'Makkah' },
    cities: [
      { id: 'makkah-city', name: { ar: 'مكة المكرمة', en: 'Makkah' }, districts: [] },
      {
        id: 'jeddah',
        name: { ar: 'جدة', en: 'Jeddah' },
        districts: [
          { id: 'jeddah-al-rawdah', name: { ar: 'الروضة', en: 'Al Rawdah' } },
          { id: 'jeddah-al-hamra', name: { ar: 'الحمراء', en: 'Al Hamra' } },
          { id: 'jeddah-al-salamah', name: { ar: 'السلامة', en: 'Al Salamah' } },
        ],
      },
      { id: 'taif', name: { ar: 'الطائف', en: 'Taif' }, districts: [] },
    ],
  },
  {
    id: 'madinah',
    name: { ar: 'المدينة المنورة', en: 'Madinah' },
    cities: [
      { id: 'madinah-city', name: { ar: 'المدينة المنورة', en: 'Madinah' }, districts: [] },
      { id: 'yanbu', name: { ar: 'ينبع', en: 'Yanbu' }, districts: [] },
    ],
  },
  {
    id: 'eastern',
    name: { ar: 'المنطقة الشرقية', en: 'Eastern Province' },
    cities: [
      {
        id: 'dammam',
        name: { ar: 'الدمام', en: 'Dammam' },
        districts: [
          { id: 'dammam-al-faisaliyah', name: { ar: 'الفيصلية', en: 'Al Faisaliyah' } },
          { id: 'dammam-al-shati', name: { ar: 'الشاطئ', en: 'Al Shati' } },
        ],
      },
      { id: 'al-khobar', name: { ar: 'الخبر', en: 'Al Khobar' }, districts: [] },
      { id: 'al-ahsa', name: { ar: 'الأحساء', en: 'Al Ahsa' }, districts: [] },
    ],
  },
  { id: 'qassim', name: { ar: 'القصيم', en: 'Al Qassim' }, cities: [{ id: 'buraydah', name: { ar: 'بريدة', en: 'Buraydah' }, districts: [] }] },
  { id: 'asir', name: { ar: 'عسير', en: 'Asir' }, cities: [{ id: 'abha', name: { ar: 'أبها', en: 'Abha' }, districts: [] }] },
  { id: 'tabuk', name: { ar: 'تبوك', en: 'Tabuk' }, cities: [{ id: 'tabuk-city', name: { ar: 'تبوك', en: 'Tabuk' }, districts: [] }] },
  { id: 'hail', name: { ar: 'حائل', en: 'Hail' }, cities: [{ id: 'hail-city', name: { ar: 'حائل', en: 'Hail' }, districts: [] }] },
  {
    id: 'northern-borders',
    name: { ar: 'الحدود الشمالية', en: 'Northern Borders' },
    cities: [{ id: 'arar', name: { ar: 'عرعر', en: 'Arar' }, districts: [] }],
  },
  { id: 'jazan', name: { ar: 'جازان', en: 'Jazan' }, cities: [{ id: 'jazan-city', name: { ar: 'جازان', en: 'Jazan' }, districts: [] }] },
  { id: 'najran', name: { ar: 'نجران', en: 'Najran' }, cities: [{ id: 'najran-city', name: { ar: 'نجران', en: 'Najran' }, districts: [] }] },
  { id: 'al-bahah', name: { ar: 'الباحة', en: 'Al Bahah' }, cities: [{ id: 'al-bahah-city', name: { ar: 'الباحة', en: 'Al Bahah' }, districts: [] }] },
  { id: 'al-jawf', name: { ar: 'الجوف', en: 'Al Jawf' }, cities: [{ id: 'sakaka', name: { ar: 'سكاكا', en: 'Sakaka' }, districts: [] }] },
];
