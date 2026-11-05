import { Injectable, computed, inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE } from '../../../patterns';
import { apiErrorFrom } from '../organizations/data/admin-api-error';
import type { ImportCommit, ImportPreview, ImportPreviewRow } from './data/import.models';
import { UserImportClient } from './data/user-import-api';
import { importFileProblem, type ImportFileProblem } from './import-file';

/** Where the import stands. */
export enum ImportStep {
  /** No preview yet: pick a workbook. */
  Pick = 1,
  /** A preview is shown; its valid rows can be committed. */
  Previewed = 2,
  /** The batch was committed; the result is shown. */
  Committed = 3,
}

/**
 * The Excel user import flow (provided per page): pick one .xlsx (checked here for type, emptiness and size), upload
 * it for a preview (every row with its verdict and reasons), then commit the batch's valid rows and show what was
 * created (with each password-bound account's activation link) and what was left out. The preview table pages on the
 * client; the server caps the rows.
 */
@Injectable()
export class UserImportStore {
  private readonly client = inject(UserImportClient);

  private readonly pickedFile = signal<File | null>(null);
  private readonly fileFailure = signal<ImportFileProblem | null>(null);
  private readonly previewResult = signal<ImportPreview | null>(null);
  private readonly commitResult = signal<ImportCommit | null>(null);
  private readonly busyNow = signal(false);
  private readonly failure = signal<ApiError | null>(null);

  readonly file = this.pickedFile.asReadonly();
  readonly fileProblem = this.fileFailure.asReadonly();
  readonly preview = this.previewResult.asReadonly();
  readonly result = this.commitResult.asReadonly();
  readonly busy = this.busyNow.asReadonly();
  /** The refusal of the last upload or commit (problem+json, with its reference code). */
  readonly error = this.failure.asReadonly();

  readonly previewPage = signal(FIRST_PAGE);
  readonly pageSize = DEFAULT_PAGE_SIZE;

  readonly step = computed(() => {
    if (this.commitResult() !== null) {
      return ImportStep.Committed;
    }
    return this.previewResult() === null ? ImportStep.Pick : ImportStep.Previewed;
  });
  readonly canPreview = computed(() => this.pickedFile() !== null && this.fileFailure() === null && !this.busyNow());
  readonly validCount = computed(() => Number(this.previewResult()?.validCount ?? 0));
  readonly invalidCount = computed(() => Number(this.previewResult()?.invalidCount ?? 0));
  readonly canCommit = computed(() => this.step() === ImportStep.Previewed && this.validCount() > 0 && !this.busyNow());
  readonly previewRows = computed<readonly ImportPreviewRow[]>(() => {
    const rows = this.previewResult()?.rows ?? [];
    const start = (this.previewPage() - FIRST_PAGE) * this.pageSize;
    return rows.slice(start, start + this.pageSize);
  });
  readonly previewTotal = computed(() => this.previewResult()?.rows.length ?? 0);

  /** A newly picked file (or none) starts over: the previous preview and result are dropped. */
  selectFile(file: File | null): void {
    this.reset();
    this.pickedFile.set(file);
    this.fileFailure.set(file === null ? null : importFileProblem(file));
  }

  async uploadPreview(): Promise<void> {
    const file = this.pickedFile();
    if (file === null || !this.canPreview()) {
      return;
    }
    const preview = await this.run(() => firstValueFrom(this.client.preview(file)));
    if (preview !== null) {
      this.previewPage.set(FIRST_PAGE);
      this.previewResult.set(preview);
    }
  }

  async commit(): Promise<void> {
    const preview = this.previewResult();
    if (preview === null || !this.canCommit()) {
      return;
    }
    const result = await this.run(() => firstValueFrom(this.client.commit(preview.batchId)));
    if (result !== null) {
      this.commitResult.set(result);
    }
  }

  /** Back to picking a file. */
  startOver(): void {
    this.selectFile(null);
  }

  private reset(): void {
    this.previewResult.set(null);
    this.commitResult.set(null);
    this.failure.set(null);
    this.previewPage.set(FIRST_PAGE);
  }

  /** Runs one call with the busy flag; a refusal is kept in {@link error} and answers null. */
  private async run<T>(call: () => Promise<T>): Promise<T | null> {
    this.busyNow.set(true);
    this.failure.set(null);
    try {
      return await call();
    } catch (error: unknown) {
      this.failure.set(apiErrorFrom(error));
      return null;
    } finally {
      this.busyNow.set(false);
    }
  }
}
