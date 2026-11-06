import type { WireId } from '@codelab/ng-security';
import type { components } from '../../core/api/generated/openapi';

/*
 * Hand-written wire shapes of the generic record and queue endpoints (Phase 7 contract, "Records HTTP API" and
 * "Queues HTTP API"). They mirror the API exactly, so these clients do not wait on the generated client; the generated
 * types under `core/api/generated` may replace them later without changing the clients' signatures. Instants are
 * ISO 8601 strings; enums travel as integers.
 */

/**
 * A record's PublicId: opaque, sent back exactly as received. Its domain varies with the record type, so a feature
 * passes its own branded id (for example `WireId<'leave-request'>`) unchanged.
 */
export type RecordId = WireId<string>;

/** A user's PublicId (the same label the Users and Access endpoints use). */
export type RecordUserId = components['schemas']['PublicIdOfUsersIds'];

/** Who is looking at the record: picks the status wording (backend `WorkflowAudience`). */
export enum WorkflowAudience {
  Requester = 1,
  Staff = 2,
  Admin = 3,
}

/** Whether an action asks for a reason (backend `ReasonRequirement`). */
export enum ReasonRequirement {
  None = 1,
  Optional = 2,
  Required = 3,
}

/** Which side took a transition (backend `TransitionSide`). */
export enum TransitionSide {
  Requester = 1,
  Staff = 2,
  System = 3,
}

/** Where a record stands against its deadline (backend `SlaState`). */
export enum SlaState {
  OnTrack = 1,
  DueSoon = 2,
  Breached = 3,
}

/** The longest reason the workflow accepts (backend `WorkflowLimits.MaxReasonLength`), in characters after trimming. */
export const WORKFLOW_MAX_REASON_LENGTH = 1000;

/** The status wording keys of one status, one per audience (translation keys). */
export interface StatusWording {
  readonly requesterKey: string;
  readonly staffKey: string;
  readonly adminKey: string;
}

/** One page of a list endpoint. `page` is 1-based. */
export interface RecordPage<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

/** One record in a list (`mine`). `number` is null until the record is submitted. */
export interface RecordListItem {
  readonly id: RecordId;
  readonly recordType: string;
  readonly number: string | null;
  readonly status: number;
  readonly statusKey: string;
  readonly createdAt: string;
  readonly submittedAt: string | null;
  readonly updatedAt: string;
  readonly isTerminal: boolean;
}

/** One record in the admin oversight list: a list item plus its owner. */
export interface OversightListItem extends RecordListItem {
  readonly ownerDisplayName: string;
}

/**
 * One action the caller may take on the record NOW, computed by the server from the workflow state, the caller's
 * permissions, ownership and audience. A screen renders these and nothing else.
 */
export interface RecordAllowedAction {
  readonly actionKey: string;
  readonly labelKey: string;
  readonly reason: ReasonRequirement;
  /** The problem+json field the server reports reason errors under; null when the action takes no reason. */
  readonly reasonFieldKey: string | null;
  readonly toStatus: number;
  readonly toTerminal: boolean;
}

/** Who currently works on the record. */
export interface RecordAssignee {
  readonly userId: RecordUserId;
  readonly displayName: string;
  readonly assignedAt: string;
}

/** The record's deadline badge, computed by the server. */
export interface RecordSla {
  readonly state: SlaState;
  readonly dueAt: string;
}

/** `GET api/records/{recordType}/{id}`: one record. `allowedActions` is empty when the record is terminal. */
export interface RecordDetail {
  readonly id: RecordId;
  readonly recordType: string;
  readonly number: string | null;
  readonly status: number;
  readonly statusKey: string;
  readonly wording: StatusWording;
  readonly isTerminal: boolean;
  readonly ownerDisplayName: string;
  readonly createdAt: string;
  readonly submittedAt: string | null;
  readonly updatedAt: string;
  readonly lastReason: string | null;
  readonly allowedActions: readonly RecordAllowedAction[];
  readonly assignee: RecordAssignee | null;
  readonly sla: RecordSla | null;
}

/** One transition in the record's history. `fromStatus` is null for the record's first entry. */
export interface TimelineEntry {
  readonly at: string;
  readonly actorDisplayName: string;
  readonly side: TransitionSide;
  readonly actionKey: string;
  readonly fromStatus: number | null;
  readonly toStatus: number;
  readonly toStatusKey: string;
  readonly reason: string | null;
}

/** The body of `POST .../transitions`. `reason` is left out when there is none. */
export interface TransitionRequest {
  readonly action: string;
  readonly reason?: string;
}

/** The queue views (`view=`): wire strings, so a constants object, not an enum. */
export const QueueView = {
  Unassigned: 'unassigned',
  Mine: 'mine',
  All: 'all',
} as const;
export type QueueView = (typeof QueueView)[keyof typeof QueueView];

/** One record in a work queue. */
export interface WorkQueueItem {
  readonly id: RecordId;
  readonly number: string;
  readonly status: number;
  readonly statusKey: string;
  readonly submittedAt: string;
  readonly ownerDisplayName: string;
  readonly assignee: RecordAssignee | null;
  readonly sla: RecordSla | null;
}

/** The body of `POST .../assign`. */
export interface AssignRequest {
  readonly userId: RecordUserId;
}

/** Paging of a list request; the server applies its defaults to what is left out. `page` is 1-based. */
export interface PageRequest {
  readonly page?: number;
  readonly pageSize?: number;
}

/** `GET api/records/{recordType}/mine`. */
export interface MineQuery extends PageRequest {
  readonly status?: number | null;
}

/** `GET api/records/{recordType}/oversight`. `number` matches part of the record number. */
export interface OversightQuery extends PageRequest {
  readonly status?: number | null;
  readonly number?: string | null;
}

/** `GET api/queues/{recordType}`. `submittedFrom` / `submittedTo` are ISO 8601 instants or dates. */
export interface QueueQuery extends PageRequest {
  readonly view: QueueView;
  readonly status?: number | null;
  readonly number?: string | null;
  readonly submittedFrom?: string | null;
  readonly submittedTo?: string | null;
}
