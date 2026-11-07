import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { DGA_CLIPBOARD } from '@codelab/ng-dga';
import { HttpStatus } from '@codelab/ng-security';
import { OpsApiClient } from '../../core/api/ops-api.client';
import type { ReferenceEvent, ReferenceTrace } from '../../core/api/ops-api.models';
import { RemoteResource } from '../../core/state/remote-resource';
import { normalizeReferenceCode, webUrlOrNull } from './reference-code';

/** The outcome of the last "copy Seq link". */
export enum CopyOutcome {
  Copied = 1,
  Failed = 2,
}

const NO_EVENTS: readonly ReferenceEvent[] = Object.freeze([]);

function byTimestamp(left: ReferenceEvent, right: ReferenceEvent): number {
  return Date.parse(left.timestamp) - Date.parse(right.timestamp);
}

/**
 * The Reference lookup screen's state. The URL's `code` query parameter is the source of truth (a lookup is a shareable
 * link): the page passes each value here. A malformed code is refused before any call; a code the logs do not hold is
 * a 404 from the BFF, shown as "not found" rather than as an error. Provided by the page.
 */
@Injectable()
export class ReferenceLookupStore {
  private readonly client = inject(OpsApiClient);
  private readonly clipboard = inject(DGA_CLIPBOARD);
  private readonly resource = new RemoteResource<ReferenceTrace>();
  private readonly lookedUp = signal<string | null>(null);
  private readonly invalidInput = signal(false);
  private readonly copy = signal<CopyOutcome | null>(null);

  readonly code = this.lookedUp.asReadonly();
  readonly invalid = this.invalidInput.asReadonly();
  readonly copyOutcome = this.copy.asReadonly();
  readonly loading = this.resource.loading;
  readonly trace = this.resource.data;
  readonly notFound = computed(() => this.resource.error()?.status === HttpStatus.notFound);
  /** A failure other than "not found" (shown with its own reference code). */
  readonly error = computed(() => (this.notFound() ? null : this.resource.error()));
  readonly events = computed(() => [...(this.trace()?.events ?? NO_EVENTS)].sort(byTimestamp));
  readonly seqUrl = computed(() => webUrlOrNull(this.trace()?.seqUrl));

  constructor() {
    inject(DestroyRef).onDestroy(() => this.resource.cancel());
  }

  /**
   * Validates what the operator typed. Returns the canonical code to put in the URL, or null (and flags the input
   * invalid) when it cannot be a reference code.
   */
  submit(input: string): string | null {
    const code = normalizeReferenceCode(input);
    this.invalidInput.set(code === null);
    return code;
  }

  /**
   * Looks up the code from the URL. Empty clears the screen; a malformed value is flagged invalid; the code already
   * shown (or loading) is not fetched again; use {@link reload} for that.
   */
  lookup(raw: string | null): void {
    const code = raw ? normalizeReferenceCode(raw) : null;
    this.invalidInput.set(Boolean(raw) && code === null);
    if (code === null) {
      this.clear();
      return;
    }
    if (code === this.lookedUp() && (this.loading() || this.trace() !== null)) {
      return;
    }
    this.fetch(code);
  }

  reload(): void {
    const code = this.lookedUp();
    if (code !== null) {
      this.fetch(code);
    }
  }

  /** Copies the Seq link of the current trace; the outcome is shown next to the button. */
  async copySeqLink(): Promise<void> {
    const url = this.seqUrl();
    if (url === null) {
      return;
    }
    this.copy.set((await this.clipboard.write(url)) ? CopyOutcome.Copied : CopyOutcome.Failed);
  }

  private fetch(code: string): void {
    this.lookedUp.set(code);
    this.copy.set(null);
    this.resource.reset();
    this.resource.load(this.client.referenceCode(code));
  }

  private clear(): void {
    this.lookedUp.set(null);
    this.copy.set(null);
    this.resource.reset();
  }
}
