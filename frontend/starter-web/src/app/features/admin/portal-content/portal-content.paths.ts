import { RoutePaths } from '../../../core/routing/route-paths';

/** The root segment every absolute in-app link starts from. */
const ROOT_SEGMENT = '/';

/** The segment of the portal content editor under `/admin` (`AdminRouteSegments.PortalContent` reuses it). */
export const PORTAL_CONTENT_MOUNT_SEGMENT = 'portal-content';

/** Absolute router commands for the portal content editor: `[routerLink]="PortalContentPaths.editor()"`. */
export const PortalContentPaths = {
  editor: (): readonly string[] => [ROOT_SEGMENT, RoutePaths.Admin, PORTAL_CONTENT_MOUNT_SEGMENT],
} as const;
