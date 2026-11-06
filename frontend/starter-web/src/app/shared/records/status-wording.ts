import { WorkflowAudience, type StatusWording } from './records.models';

/** The last segment of an audience's wording key (`<prefix>.<statusKey>.<segment>`), as the backend catalog writes it. */
export const AUDIENCE_WORDING_SEGMENTS: Readonly<Record<WorkflowAudience, string>> = {
  [WorkflowAudience.Requester]: 'requester',
  [WorkflowAudience.Staff]: 'staff',
  [WorkflowAudience.Admin]: 'admin',
};

/** Maps a status key (`under-review`) and an audience to the translation key of its wording. */
export type StatusWordingResolver = (statusKey: string, audience: WorkflowAudience) => string;

const KEY_SEPARATOR = '.';

/**
 * The translation key of `wording` for `audience` (the requester reads "Being processed" where staff read
 * "Under review"). An audience outside {@link WorkflowAudience} falls back to the requester's wording, the least
 * revealing one.
 */
export function statusWordingKey(wording: StatusWording, audience: WorkflowAudience): string {
  switch (audience) {
    case WorkflowAudience.Staff:
      return wording.staffKey;
    case WorkflowAudience.Admin:
      return wording.adminKey;
    default:
      return wording.requesterKey;
  }
}

/**
 * A resolver for a record type whose wording keys follow the catalog convention `<prefix>.<statusKey>.<audience>`,
 * for the timeline (whose entries carry only the status key). Example: `statusWordingResolver('things.status')`
 * maps (`approved`, Staff) to `things.status.approved.staff`.
 */
export function statusWordingResolver(prefix: string): StatusWordingResolver {
  return (statusKey, audience) =>
    [prefix, statusKey, AUDIENCE_WORDING_SEGMENTS[audience] ?? AUDIENCE_WORDING_SEGMENTS[WorkflowAudience.Requester]]
      .join(KEY_SEPARATOR);
}
