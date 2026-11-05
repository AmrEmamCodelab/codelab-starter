import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { FeaturesClient } from '../api/clients/features.client';

/**
 * The feature flags the frontend reads (from the API's `Features:<Flag>` options, delivered by
 * `GET api/features`). Names are the JSON keys the API returns.
 */
export const FeatureFlagNames = {
  /** Load the session-gated half of the translations from `api/i18n` after login. */
  ProtectedTranslations: 'protectedTranslations',
} as const;

export type FeatureFlagName = (typeof FeatureFlagNames)[keyof typeof FeatureFlagNames];

/**
 * The deployment's feature flags. Flags only, never identities, so they load before login. A flag the
 * server did not send is off, and a failed load leaves every flag off: a flag only ever adds behaviour,
 * so "off" is the safe reading.
 */
@Injectable({ providedIn: 'root' })
export class FeatureStore {
  private readonly client = inject(FeaturesClient);
  private readonly values = signal<Readonly<Record<string, boolean>>>({});
  private readonly done = signal(false);

  readonly flags = this.values.asReadonly();
  /** True once a load has completed (successfully or not). */
  readonly loaded = this.done.asReadonly();

  isEnabled(name: string): boolean {
    return this.values()[name] === true;
  }

  async load(): Promise<void> {
    try {
      const response = await firstValueFrom(this.client.flags());
      this.values.set({ ...response.flags });
    } catch {
      // Best effort: the app runs with every flag off rather than failing to start.
      this.values.set({});
    } finally {
      this.done.set(true);
    }
  }
}
