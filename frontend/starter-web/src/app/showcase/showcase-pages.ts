import { RoutePaths } from '../core/routing/route-paths';
import { ShowcaseTranslationKeys } from './showcase-translation-keys';

/** The showcase page groups, in the order the showcase navigation lists them. */
export const ShowcaseGroup = {
  Components: 'components',
  Patterns: 'patterns',
  Layouts: 'layouts',
  Branding: 'branding',
} as const;

export type ShowcaseGroup = (typeof ShowcaseGroup)[keyof typeof ShowcaseGroup];

/** Stable ids of the showcase pages (route registry keys, visual-baseline names). */
export const ShowcasePageId = {
  Components: 'components',
  AdminList: 'admin-list',
  TreeEditor: 'tree-editor',
  WorkQueue: 'work-queue',
  StaffDetail: 'staff-detail',
  Wizard: 'wizard',
  RequesterDetail: 'requester-detail',
  PortalContent: 'portal-content',
  LandingDashboard: 'landing-dashboard',
  Layouts: 'layouts',
  PortalShell: 'portal-shell',
  RequesterShell: 'requester-shell',
  Branding: 'branding',
} as const;

export type ShowcasePageId = (typeof ShowcasePageId)[keyof typeof ShowcasePageId];

/** One showcase page: where it lives under `/showcase` and how the navigation names it. */
export interface ShowcasePage {
  readonly id: ShowcasePageId;
  readonly group: ShowcaseGroup;
  /** The path below `RoutePaths.Showcase`; '' is `/showcase` itself (the component gallery the catalog links to). */
  readonly segment: string;
  readonly titleKey: string;
  readonly introKey: string;
}

const Pages = ShowcaseTranslationKeys.Pages;
const Intros = ShowcaseTranslationKeys.Intros;
const PATTERNS_SEGMENT = 'patterns';
const LAYOUTS_SEGMENT = 'layouts';

function under(folder: string, name: string): string {
  return `${folder}/${name}`;
}

/**
 * Every showcase page in navigation order. The component gallery sits at `/showcase` so the catalog's
 * `/showcase#<selector>` links land on it; each page pattern and layout frame gets its own page so a full-page
 * screenshot stays a readable size.
 */
export const SHOWCASE_PAGES: readonly ShowcasePage[] = [
  { id: ShowcasePageId.Components, group: ShowcaseGroup.Components, segment: '', titleKey: Pages.Components, introKey: Intros.Components },
  { id: ShowcasePageId.AdminList, group: ShowcaseGroup.Patterns, segment: under(PATTERNS_SEGMENT, ShowcasePageId.AdminList), titleKey: Pages.AdminList, introKey: Intros.AdminList },
  { id: ShowcasePageId.TreeEditor, group: ShowcaseGroup.Patterns, segment: under(PATTERNS_SEGMENT, ShowcasePageId.TreeEditor), titleKey: Pages.TreeEditor, introKey: Intros.TreeEditor },
  { id: ShowcasePageId.WorkQueue, group: ShowcaseGroup.Patterns, segment: under(PATTERNS_SEGMENT, ShowcasePageId.WorkQueue), titleKey: Pages.WorkQueue, introKey: Intros.WorkQueue },
  { id: ShowcasePageId.StaffDetail, group: ShowcaseGroup.Patterns, segment: under(PATTERNS_SEGMENT, ShowcasePageId.StaffDetail), titleKey: Pages.StaffDetail, introKey: Intros.StaffDetail },
  { id: ShowcasePageId.Wizard, group: ShowcaseGroup.Patterns, segment: under(PATTERNS_SEGMENT, ShowcasePageId.Wizard), titleKey: Pages.Wizard, introKey: Intros.Wizard },
  { id: ShowcasePageId.RequesterDetail, group: ShowcaseGroup.Patterns, segment: under(PATTERNS_SEGMENT, ShowcasePageId.RequesterDetail), titleKey: Pages.RequesterDetail, introKey: Intros.RequesterDetail },
  { id: ShowcasePageId.PortalContent, group: ShowcaseGroup.Patterns, segment: under(PATTERNS_SEGMENT, ShowcasePageId.PortalContent), titleKey: Pages.PortalContent, introKey: Intros.PortalContent },
  { id: ShowcasePageId.LandingDashboard, group: ShowcaseGroup.Patterns, segment: under(PATTERNS_SEGMENT, ShowcasePageId.LandingDashboard), titleKey: Pages.LandingDashboard, introKey: Intros.LandingDashboard },
  { id: ShowcasePageId.Layouts, group: ShowcaseGroup.Layouts, segment: LAYOUTS_SEGMENT, titleKey: Pages.Layouts, introKey: Intros.Layouts },
  { id: ShowcasePageId.PortalShell, group: ShowcaseGroup.Layouts, segment: under(LAYOUTS_SEGMENT, ShowcasePageId.PortalShell), titleKey: Pages.PortalShell, introKey: Intros.PortalShell },
  { id: ShowcasePageId.RequesterShell, group: ShowcaseGroup.Layouts, segment: under(LAYOUTS_SEGMENT, ShowcasePageId.RequesterShell), titleKey: Pages.RequesterShell, introKey: Intros.RequesterShell },
  { id: ShowcasePageId.Branding, group: ShowcaseGroup.Branding, segment: ShowcaseGroup.Branding, titleKey: Pages.Branding, introKey: Intros.Branding },
];

/** The full route path (no leading slash) of a showcase page: `showcase` or `showcase/<segment>`. */
export function showcaseFullPath(segment: string): string {
  return segment === '' ? RoutePaths.Showcase : `${RoutePaths.Showcase}/${segment}`;
}

/** The absolute router link of a showcase page (`/showcase/patterns/wizard`). */
export function showcaseLink(id: ShowcasePageId): string {
  return `/${showcaseFullPath(showcasePage(id).segment)}`;
}

/** The registry entry of a page. @throws Error for an id that has no page (a registry bug, caught by the spec). */
export function showcasePage(id: ShowcasePageId): ShowcasePage {
  const page = SHOWCASE_PAGES.find(candidate => candidate.id === id);
  if (page === undefined) {
    throw new Error(`No showcase page '${id}'.`);
  }
  return page;
}

/** The pages of one group, in navigation order. */
export function showcasePagesIn(group: ShowcaseGroup): readonly ShowcasePage[] {
  return SHOWCASE_PAGES.filter(page => page.group === group);
}
