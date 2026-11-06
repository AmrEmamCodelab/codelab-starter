import { RoutePaths } from '../../../core/routing/route-paths';

/** The root segment every absolute in-app link starts from. */
const ROOT_SEGMENT = '/';

/** The mount segment of the lookups screens under `/admin` (`AdminRouteSegments.Lookups` reuses it). */
export const LOOKUPS_MOUNT_SEGMENT = 'lookups';

/** The route parameters of the lookups screens. */
export const LookupsRouteParams = {
  /** The lookup type key (`contact-topics`), as `GET api/admin/lookups` lists it. */
  Type: 'type',
} as const;

/** The paths of `LOOKUPS_ROUTES`, relative to the `/admin` mount. */
export const LookupsRouteSegments = {
  Types: LOOKUPS_MOUNT_SEGMENT,
  Items: `${LOOKUPS_MOUNT_SEGMENT}/:${LookupsRouteParams.Type}`,
} as const;

/** Absolute router commands for the lookups screens: `[routerLink]="LookupsPaths.items(type.type)"`. */
export const LookupsPaths = {
  types: (): readonly string[] => [ROOT_SEGMENT, RoutePaths.Admin, LOOKUPS_MOUNT_SEGMENT],
  items: (type: string): readonly string[] => [ROOT_SEGMENT, RoutePaths.Admin, LOOKUPS_MOUNT_SEGMENT, type],
} as const;
