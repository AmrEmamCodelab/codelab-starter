import type { DgaStatusTone } from '@codelab/ng-dga';
import { RecordsTranslationKeys } from './records-translation-keys';
import { SlaState, type RecordSla } from './records.models';

/** How an SLA state looks: its label and status-tag tone. */
export interface SlaDisplay {
  readonly labelKey: string;
  readonly tone: DgaStatusTone;
}

/** The label and tone of each SLA state. The server computes the state; the badge never compares clocks. */
export const SLA_STATE_DISPLAY: Readonly<Record<SlaState, SlaDisplay>> = {
  [SlaState.OnTrack]: { labelKey: RecordsTranslationKeys.Sla.OnTrack, tone: 'success' },
  [SlaState.DueSoon]: { labelKey: RecordsTranslationKeys.Sla.DueSoon, tone: 'warning' },
  [SlaState.Breached]: { labelKey: RecordsTranslationKeys.Sla.Breached, tone: 'error' },
};

/** The display of a record's SLA, or null when it has none or the state is not one the app knows (nothing shows). */
export function slaDisplay(sla: RecordSla | null | undefined): SlaDisplay | null {
  if (!sla) {
    return null;
  }
  return SLA_STATE_DISPLAY[sla.state] ?? null;
}
