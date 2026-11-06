import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslationService } from '../../../core/i18n/translation.service';
import { localizeSections, PortalContentStatus } from './portal-content';
import { PortalContentClient, type PortalContentDto, type PortalContentPage } from './portal-content.client';

/**
 * One portal page's content: loads it from the API, keeps its status and serves the sections in the active language
 * (switching the language re-renders without a new request). Provided per page.
 */
@Injectable()
export class PortalContentStore {
  private readonly client = inject(PortalContentClient);
  private readonly translations = inject(TranslationService);
  private readonly content = signal<PortalContentDto | null>(null);
  private readonly state = signal(PortalContentStatus.Loading);
  private page: PortalContentPage | null = null;
  /** Bumped on every load, so an answer that arrives after a newer load started is ignored. */
  private generation = 0;

  readonly status = this.state.asReadonly();
  /** The sections in the active language, in `sortOrder`; empty until loaded. */
  readonly sections = computed(() =>
    localizeSections(this.content()?.sections ?? [], this.translations.language()),
  );
  /** True once loaded with no section to show (the page shows its empty state). */
  readonly empty = computed(() => this.state() === PortalContentStatus.Loaded && this.sections().length === 0);

  /**
   * Loads `page` (or reloads it). Never rejects: a failure sets {@link status} to `Failed`, which the page shows with
   * a retry.
   */
  async load(page: PortalContentPage): Promise<void> {
    this.page = page;
    const generation = ++this.generation;
    this.state.set(PortalContentStatus.Loading);
    try {
      const content = await firstValueFrom(this.client.page(page));
      if (generation === this.generation) {
        this.content.set(content);
        this.state.set(PortalContentStatus.Loaded);
      }
    } catch {
      // The page renders the failure (self-handled call): the reader sees the error state and can retry.
      if (generation === this.generation) {
        this.content.set(null);
        this.state.set(PortalContentStatus.Failed);
      }
    }
  }

  /** Loads the last requested page again (the error state's retry); does nothing before a first load. */
  retry(): Promise<void> {
    return this.page === null ? Promise.resolve() : this.load(this.page);
  }
}
