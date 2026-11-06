/** The portal pages whose content the admin edits; the values are the `{page}` route segment of the API. */
export const PortalContentPages = {
  Faq: 'faq',
  ServiceGuide: 'service-guide',
  Privacy: 'privacy',
  Terms: 'terms',
} as const;

export type PortalContentPage = (typeof PortalContentPages)[keyof typeof PortalContentPages];

/** The pages in the order the selector offers them. */
export const PORTAL_CONTENT_PAGES: readonly PortalContentPage[] = [
  PortalContentPages.Faq,
  PortalContentPages.ServiceGuide,
  PortalContentPages.Privacy,
  PortalContentPages.Terms,
];

/** The translation key of each page's selector label. */
export const PortalContentPageLabelKeys: Readonly<Record<PortalContentPage, string>> = {
  [PortalContentPages.Faq]: 'admin.portal-content.pages.faq',
  [PortalContentPages.ServiceGuide]: 'admin.portal-content.pages.service-guide',
  [PortalContentPages.Privacy]: 'admin.portal-content.pages.privacy',
  [PortalContentPages.Terms]: 'admin.portal-content.pages.terms',
};

export function isPortalContentPage(value: unknown): value is PortalContentPage {
  return typeof value === 'string' && (PORTAL_CONTENT_PAGES as readonly string[]).includes(value);
}

/**
 * The client copy of the backend's portal content limits (`PortalContentLimits`); the server stays the authority
 * and refuses anything longer with problem+json field errors.
 */
export const PortalContentLimits = {
  TitleMaxLength: 200,
  BodyMaxLength: 4000,
  MaxSections: 50,
} as const;

/** The field names of a section, as the API names them (and its problem+json `errors` keys end with). */
export const SectionFields = {
  TitleAr: 'titleAr',
  TitleEn: 'titleEn',
  BodyAr: 'bodyAr',
  BodyEn: 'bodyEn',
  SortOrder: 'sortOrder',
} as const;

/** One stored section (plain text; paragraphs separated by blank lines). */
export interface PortalContentSection {
  /** Opaque PublicId string of the stored section. */
  readonly id: string;
  readonly titleAr: string;
  readonly titleEn: string;
  readonly bodyAr: string;
  readonly bodyEn: string;
  readonly sortOrder: number;
}

/** One page's content, as `GET api/admin/portal/content/{page}` answers it. */
export interface PortalContentDocument {
  readonly page: string;
  /** ISO 8601 UTC time of the last save, or null when the page was never saved. */
  readonly updatedAt: string | null;
  readonly sections: readonly PortalContentSection[];
}

/** One section of the replace body: `id` null creates it, a known id keeps it; a missing id deletes it. */
export interface PortalContentSectionInput {
  readonly id: string | null;
  readonly titleAr: string;
  readonly titleEn: string;
  readonly bodyAr: string;
  readonly bodyEn: string;
  readonly sortOrder: number;
}

/** The body of `PUT api/admin/portal/content/{page}`: the page's full, ordered section list. */
export interface ReplacePortalContentBody {
  readonly sections: readonly PortalContentSectionInput[];
}
