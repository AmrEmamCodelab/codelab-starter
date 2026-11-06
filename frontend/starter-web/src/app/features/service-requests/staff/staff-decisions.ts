import type { AllowedAction, ReasonLimits } from '../../../patterns';
import {
  ReasonRequirement,
  WORKFLOW_MAX_REASON_LENGTH,
  type RecordAllowedAction,
  type RecordAssignee,
  type RecordUserId,
} from '../../../shared/records';
import { serviceRequestActionTone } from './service-request-status-display';

/** The shortest reason the workflow accepts once trimmed: any non-blank text (the server checks `Required` only). */
export const STAFF_REASON_MIN_LENGTH = 1;

/** The reason limits of the decision dialog; they equal the workflow's (`WorkflowLimits.MaxReasonLength`). */
export const STAFF_REASON_LIMITS: ReasonLimits = {
  minLength: STAFF_REASON_MIN_LENGTH,
  maxLength: WORKFLOW_MAX_REASON_LENGTH,
};

/**
 * The server's allowed actions as the staff-detail pattern's decision buttons, in the server's order (the pattern
 * orders them by tone). Nothing is added or filtered: the list IS the permission and status gate. An action whose
 * reason is Required opens the decision dialog first (`requiresReason`).
 */
export function staffDecisionButtons(actions: readonly RecordAllowedAction[]): readonly AllowedAction[] {
  return actions.map((action) => ({
    code: action.actionKey,
    labelKey: action.labelKey,
    requiresReason: action.reason === ReasonRequirement.Required,
    tone: serviceRequestActionTone(action.actionKey),
  }));
}

/** The server's allowed action with `actionKey`, or null when it is no longer allowed. */
export function findRecordAction(actions: readonly RecordAllowedAction[], actionKey: string): RecordAllowedAction | null {
  return actions.find((action) => action.actionKey === actionKey) ?? null;
}

/** Who holds a record in the queue, seen from the signed-in user. Integers, like every enum the starter owns. */
export enum AssignmentHolder {
  /** Nobody: the user may claim it. */
  Nobody = 1,
  /** The signed-in user: they may release it. */
  CurrentUser = 2,
  /** Someone else. */
  SomeoneElse = 3,
}

/** Who holds the record: nobody, the signed-in user (`currentUserId`, their PublicId) or someone else. */
export function assignmentHolder(assignee: RecordAssignee | null, currentUserId: RecordUserId | string | null): AssignmentHolder {
  if (assignee === null) {
    return AssignmentHolder.Nobody;
  }
  return currentUserId !== null && assignee.userId === currentUserId ? AssignmentHolder.CurrentUser : AssignmentHolder.SomeoneElse;
}
